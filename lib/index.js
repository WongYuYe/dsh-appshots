import { spawn } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { mkdir, readdir, readFile, rm, stat, unlink, writeFile } from 'node:fs/promises'
import { homedir, tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createInterface } from 'node:readline'
import z from '@deepseek-ai/schemastery'
import { createUserMessage } from '@deepseek-ai/dsh-llm'
import { installSettingsSection, settingsNamespace } from '@deepseek-ai/dsh-settings'

export const name = 'dsh-appshots'
export const inject = ['webServer']
export const Config = z.object({
  skipSelf: z.boolean().default(true),
  attachText: z.boolean().default(true),
  recentWindowMs: z.number().min(0).max(600000).default(60000),
  hotkeyMode: z.string().default('both-command'),
  carbonKeyCode: z.number().min(0).max(127).default(0),
  carbonModifiers: z.number().min(0).max(65535).default(256),
  maxTextChars: z.number().min(0).max(200000).default(12000),
})

export const SETTINGS_NS = settingsNamespace('dsh-appshots')

const ROOT = dirname(fileURLToPath(import.meta.url))
const HELPER = join(ROOT, '..', 'bin', 'appshot-capture')
const PENDING_TTL_MS = 5 * 60 * 1000
const MAX_PENDING = 8

function dshHome() {
  return process.env.DSH_HOME?.trim() || join(homedir(), '.dsh')
}

function pendingDir() {
  return join(dshHome(), 'appshots', 'pending')
}

function header(headers, key) {
  const value = headers[key]
  return typeof value === 'string' ? value : undefined
}

function isLoopbackHostname(hostname) {
  if (hostname === 'localhost' || hostname === '[::1]') return true
  const parts = hostname.split('.')
  return parts.length === 4 && parts[0] === '127' && parts.every((part) => /^\d{1,3}$/.test(part) && Number(part) <= 255)
}

export function isTrustedRequest(req) {
  const host = header(req.headers, 'host')
  if (host === undefined) return false
  let hostname
  try {
    hostname = new URL(`http://${host}`).hostname
  } catch {
    return false
  }
  if (!isLoopbackHostname(hostname)) return false
  if (header(req.headers, 'sec-fetch-site') === 'cross-site') return false
  const origin = header(req.headers, 'origin')
  if (origin === undefined) return true
  try {
    return new URL(origin).host === host
  } catch {
    return false
  }
}

function sendJson(res, status, value) {
  const body = JSON.stringify(value)
  res.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'content-length': Buffer.byteLength(body),
    'cache-control': 'no-store',
  })
  res.end(body)
}

function methodNotAllowed(res, methods) {
  res.writeHead(405, { allow: methods.join(', '), 'content-length': 0 })
  res.end()
}

async function readJsonBody(req, maxBytes = 64 * 1024) {
  const chunks = []
  let size = 0
  for await (const chunk of req) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk)
    size += buffer.byteLength
    if (size > maxBytes) throw new Error('request too large')
    chunks.push(buffer)
  }
  if (chunks.length === 0) return {}
  return JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}')
}

function runHelper(args, { timeoutMs = 8000 } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(HELPER, args, { stdio: ['ignore', 'pipe', 'pipe'] })
    let stdout = ''
    let stderr = ''
    const timer = setTimeout(() => {
      child.kill('SIGKILL')
      reject(new Error('appshot helper timed out'))
    }, timeoutMs)
    child.stdout.on('data', (chunk) => { stdout += chunk.toString('utf8') })
    child.stderr.on('data', (chunk) => { stderr += chunk.toString('utf8') })
    child.on('error', (error) => {
      clearTimeout(timer)
      reject(error)
    })
    child.on('exit', (code) => {
      clearTimeout(timer)
      const line = stdout.trim().split('\n').filter(Boolean).at(-1) || ''
      if (!line) {
        reject(new Error(stderr.trim() || `appshot helper exited ${code ?? 'unknown'}`))
        return
      }
      try {
        resolve(JSON.parse(line))
      } catch {
        reject(new Error(stderr.trim() || 'appshot helper returned invalid JSON'))
      }
    })
  })
}

function runScreencapture(windowId, outPath) {
  return new Promise((resolve, reject) => {
    const child = spawn('/usr/sbin/screencapture', ['-x', '-o', `-l${windowId}`, outPath], {
      stdio: 'ignore',
    })
    const timer = setTimeout(() => {
      child.kill('SIGKILL')
      reject(new Error('screencapture timed out'))
    }, 8000)
    child.on('error', (error) => {
      clearTimeout(timer)
      reject(error)
    })
    child.on('exit', (code) => {
      clearTimeout(timer)
      if (code === 0) resolve()
      else reject(new Error(`screencapture exited ${code}. Grant Screen Recording to DSH Desktop.`))
    })
  })
}

function contextDir() {
  return join(dshHome(), 'appshots', 'context')
}

function contextPath(sessionId) {
  return join(contextDir(), `${sessionId}.json`)
}

function renderHiddenText(items) {
  const blocks = items.flatMap((item, index) => {
    const body = String(item.text || '').trim()
    if (!body) return []
    const title = [item.owner, item.title].filter(Boolean).join(' — ')
    const heading = `Appshot ${index + 1}${title ? `: ${title}` : ''}`
    const truncated = item.textTruncated ? '\n(window text truncated)' : ''
    return [`${heading}\n${body}${truncated}`]
  })
  if (blocks.length === 0) return ''
  return [
    'Hidden Appshot window text for the attached screenshot(s). Prefer the image; use this text only to read off-screen or small copy.',
    '',
    ...blocks,
  ].join('\n')
}

export function apply(ctx, config) {
  const live = { ...Config(config ?? {}) }
  const pending = new Map()
  const hiddenBySession = new Map()
  const sseClients = new Set()
  let hotkey = null
  let hotkeyTimer = null
  let lastHotkeyCapture = 0

  const broadcast = (payload) => {
    const frame = `data: ${JSON.stringify(payload)}\n\n`
    for (const res of sseClients) {
      try { res.write(frame) } catch { sseClients.delete(res) }
    }
  }

  const persistHidden = async (sessionId) => {
    const items = hiddenBySession.get(sessionId) || []
    const path = contextPath(sessionId)
    if (items.length === 0) {
      await rm(path, { force: true }).catch(() => {})
      return
    }
    await mkdir(contextDir(), { recursive: true })
    await writeFile(path, JSON.stringify({ sessionId, items }))
  }

  const forgetHiddenId = async (id) => {
    for (const [sessionId, items] of [...hiddenBySession.entries()]) {
      const next = items.filter((item) => item.id !== id)
      if (next.length === items.length) continue
      if (next.length === 0) hiddenBySession.delete(sessionId)
      else hiddenBySession.set(sessionId, next)
      await persistHidden(sessionId)
    }
  }

  const pruneHidden = async (sessionId) => {
    const items = hiddenBySession.get(sessionId) || []
    const fresh = items.filter((item) => Date.now() - Number(item.createdAt || 0) <= PENDING_TTL_MS)
    if (fresh.length === items.length) return fresh
    if (fresh.length === 0) hiddenBySession.delete(sessionId)
    else hiddenBySession.set(sessionId, fresh)
    await persistHidden(sessionId)
    return fresh
  }

  const rememberHidden = async (sessionId, item) => {
    if (!sessionId || !live.attachText) return
    const createdAt = item.createdAt || Date.now()
    const next = [...(await pruneHidden(sessionId)), {
      id: item.id,
      owner: item.owner,
      title: item.title,
      text: item.text,
      textTruncated: item.textTruncated,
      createdAt,
    }].slice(-MAX_PENDING)
    hiddenBySession.set(sessionId, next)
    await persistHidden(sessionId)
    const remaining = Math.max(5_000, PENDING_TTL_MS - (Date.now() - createdAt))
    const timer = setTimeout(() => { void forgetHiddenId(item.id) }, remaining)
    timer.unref?.()
    item.hiddenTimer = timer
  }

  const takeHidden = async (sessionId) => {
    const items = await pruneHidden(sessionId)
    hiddenBySession.delete(sessionId)
    await persistHidden(sessionId)
    return items
  }

  const restoreHidden = async () => {
    let names = []
    try { names = await readdir(contextDir()) } catch { return }
    for (const name of names) {
      if (!name.endsWith('.json')) continue
      try {
        const payload = JSON.parse(await readFile(join(contextDir(), name), 'utf8'))
        const sessionId = String(payload.sessionId || name.slice(0, -5))
        const items = (Array.isArray(payload.items) ? payload.items : []).filter((item) => (
          Date.now() - Number(item.createdAt || 0) <= PENDING_TTL_MS
        ))
        if (sessionId && items.length > 0) {
          hiddenBySession.set(sessionId, items)
          await persistHidden(sessionId)
          for (const item of items) {
            const remaining = Math.max(5_000, PENDING_TTL_MS - (Date.now() - Number(item.createdAt || Date.now())))
            const timer = setTimeout(() => { void forgetHiddenId(item.id) }, remaining)
            timer.unref?.()
          }
        } else {
          await rm(join(contextDir(), name), { force: true }).catch(() => {})
        }
      } catch {}
    }
  }

  const forget = async (id) => {
    const item = pending.get(id)
    if (!item) return
    pending.delete(id)
    clearTimeout(item.timer)
    await rm(item.imagePath, { force: true }).catch(() => {})
    await rm(item.metaPath, { force: true }).catch(() => {})
  }

  const armPending = (item) => {
    const remaining = Math.max(5_000, PENDING_TTL_MS - (Date.now() - Number(item.createdAt || Date.now())))
    const timer = setTimeout(() => { void forget(item.id) }, remaining)
    timer.unref?.()
    pending.set(item.id, { ...item, timer })
    return item.id
  }

  const restorePending = async () => {
    const dir = pendingDir()
    let names = []
    try { names = await readdir(dir) } catch { return }
    const metas = names.filter((name) => name.endsWith('.json')).sort()
    for (const name of metas) {
      const id = name.slice(0, -5)
      if (pending.has(id)) continue
      const metaPath = join(dir, name)
      const imagePath = join(dir, `${id}.png`)
      try {
        const meta = JSON.parse(await readFile(metaPath, 'utf8'))
        const info = await stat(imagePath)
        if (info.size < 32) continue
        if (Date.now() - Number(meta.createdAt || 0) > PENDING_TTL_MS) {
          await rm(imagePath, { force: true }).catch(() => {})
          await rm(metaPath, { force: true }).catch(() => {})
          continue
        }
        armPending({
          id,
          owner: String(meta.owner || ''),
          title: String(meta.title || ''),
          pid: Number(meta.pid || 0),
          windowId: Number(meta.windowId || 0),
          text: String(meta.text || ''),
          textTruncated: Boolean(meta.textTruncated),
          axTrusted: Boolean(meta.axTrusted),
          createdAt: Number(meta.createdAt || Date.now()),
          imagePath,
          metaPath,
        })
      } catch {}
    }
  }

  const remember = async (shot) => {
    while (pending.size >= MAX_PENDING) {
      const oldest = pending.keys().next().value
      await forget(oldest)
    }
    const id = randomUUID()
    const dir = pendingDir()
    await mkdir(dir, { recursive: true })
    const imagePath = join(dir, `${id}.png`)
    const metaPath = join(dir, `${id}.json`)
    await writeFile(imagePath, shot.bytes)
    const meta = {
      id,
      owner: shot.owner,
      title: shot.title,
      pid: shot.pid,
      windowId: shot.windowId,
      text: shot.text,
      textTruncated: shot.textTruncated,
      axTrusted: shot.axTrusted,
      createdAt: Date.now(),
    }
    await writeFile(metaPath, JSON.stringify(meta))
    return armPending({ ...meta, imagePath, metaPath })
  }

  const captureShot = async ({ skipSelf, includeSelf } = {}) => {
    if (process.platform !== 'darwin') {
      throw new Error('Appshots currently support macOS only')
    }
    const skip = includeSelf === true ? false : (skipSelf ?? live.skipSelf)
    const front = await runHelper([
      'front',
      ...(skip ? [] : ['--include-self']),
      '--max-chars',
      String(live.maxTextChars),
    ])
    if (front?.ok !== true) throw new Error(front?.error || 'failed to inspect front window')
    const imagePath = join(tmpdir(), `dsh-appshot-${randomUUID()}.png`)
    try {
      await runScreencapture(front.windowId, imagePath)
      const info = await stat(imagePath)
      if (info.size < 32) throw new Error('screenshot was empty. Grant Screen Recording to DSH Desktop.')
      const bytes = await readFile(imagePath)
      return {
        owner: String(front.owner || ''),
        title: String(front.title || ''),
        pid: Number(front.pid || 0),
        windowId: Number(front.windowId || 0),
        text: live.attachText ? String(front.text || '') : '',
        textTruncated: Boolean(front.textTruncated),
        axTrusted: Boolean(front.axTrusted),
        bytes,
      }
    } finally {
      await unlink(imagePath).catch(() => {})
    }
  }

  const activateDesktop = () => {
    if (process.platform !== 'darwin') return
    void runHelper(['activate'], { timeoutMs: 2500 }).catch((error) => {
      ctx.logger?.warn?.(`[dsh-appshots] activate: ${error instanceof Error ? error.message : String(error)}`)
    })
  }

  const captureAndStore = async (options) => {
    const shot = await captureShot(options)
    const id = await remember(shot)
    const item = pending.get(id)
    broadcast({ type: 'captured', id, owner: item.owner, title: item.title })
    activateDesktop()
    return item
  }

  const stopHotkey = () => {
    if (!hotkey) return
    try { hotkey.kill() } catch {}
    hotkey = null
  }

  const startHotkeyNow = () => {
    if (process.platform !== 'darwin' || live.hotkeyMode === 'off') {
      stopHotkey()
      return
    }
    stopHotkey()
    const args = live.hotkeyMode === 'carbon'
      ? ['hotkey', '--mode', 'carbon', '--key-code', String(live.carbonKeyCode), '--modifiers', String(live.carbonModifiers)]
      : ['hotkey', '--mode', 'both-command']
    const child = spawn(HELPER, args, { stdio: ['ignore', 'pipe', 'pipe'] })
    hotkey = child
    const rl = createInterface({ input: child.stdout })
    rl.on('line', (line) => {
      let parsed
      try { parsed = JSON.parse(line) } catch { return }
      if (parsed?.event === 'hotkey') {
        const now = Date.now()
        if (now - lastHotkeyCapture < 600) return
        lastHotkeyCapture = now
        void captureAndStore().catch((error) => {
          broadcast({ type: 'error', error: error instanceof Error ? error.message : String(error) })
        })
      } else if (parsed?.event === 'ready') {
        broadcast({ type: 'hotkey-ready', mode: parsed.mode || live.hotkeyMode })
      } else if (parsed?.ok === false && parsed?.error) {
        broadcast({ type: 'error', error: parsed.error })
      }
    })
    child.stderr.on('data', (chunk) => {
      ctx.logger?.warn?.(`[dsh-appshots] hotkey: ${chunk.toString('utf8').trim()}`)
    })
    child.on('exit', (code) => {
      rl.close()
      if (hotkey === child) hotkey = null
      if (code && code !== 0) ctx.logger?.warn?.(`[dsh-appshots] hotkey helper exited ${code}`)
    })
    child.on('error', (error) => {
      ctx.logger?.warn?.(`[dsh-appshots] hotkey helper failed: ${error.message}`)
    })
  }

  const startHotkey = () => {
    if (hotkeyTimer) clearTimeout(hotkeyTimer)
    hotkeyTimer = setTimeout(() => {
      hotkeyTimer = null
      startHotkeyNow()
    }, 150)
  }

  let settingsSource = () => live
  const refreshLive = () => {
    Object.assign(live, Config(settingsSource() ?? {}))
  }
  installSettingsSection(ctx, SETTINGS_NS, Config, live, {
    setSource: (current) => {
      settingsSource = current
      refreshLive()
    },
    onChange: () => {
      refreshLive()
      startHotkey()
    },
  })

  ctx.inject(['webServer'], (sctx) => {
    const dispose = sctx.webServer.register({
      kind: 'prefix',
      path: '/api/appshots',
      handler: async (req, res) => {
        if (!isTrustedRequest(req)) return sendJson(res, 403, { ok: false, error: 'forbidden origin' })
        const url = new URL(req.url || '/', 'http://dsh.internal')
        const pathname = url.pathname
        try {
          if (pathname === '/api/appshots/status' && req.method === 'GET') {
            let helper = { ok: false }
            try { helper = await runHelper(['status'], { timeoutMs: 4000 }) } catch (error) {
              helper = { ok: false, error: error instanceof Error ? error.message : String(error) }
            }
            return sendJson(res, 200, {
              ok: true,
              platform: process.platform,
              hotkeyMode: live.hotkeyMode,
              skipSelf: live.skipSelf,
              attachText: live.attachText,
              recentWindowMs: live.recentWindowMs,
              helper,
            })
          }
          if (pathname === '/api/appshots/capture' && req.method === 'POST') {
            const body = await readJsonBody(req).catch(() => ({}))
            const item = await captureAndStore({ includeSelf: body.includeSelf === true })
            return sendJson(res, 200, { ok: true, id: item.id, owner: item.owner, title: item.title })
          }
          if (pathname === '/api/appshots/bind' && req.method === 'POST') {
            const body = await readJsonBody(req).catch(() => ({}))
            const sessionId = String(body.sessionId || '')
            const id = String(body.id || '')
            const item = pending.get(id)
            if (!sessionId || !item) return sendJson(res, 404, { ok: false, error: 'appshot expired' })
            await rememberHidden(sessionId, item)
            return sendJson(res, 200, { ok: true })
          }
          if (pathname === '/api/appshots/pending' && req.method === 'GET') {
            const items = [...pending.values()].map((item) => ({
              id: item.id,
              owner: item.owner,
              title: item.title,
              createdAt: item.createdAt,
            }))
            return sendJson(res, 200, { ok: true, items })
          }
          if (pathname.startsWith('/api/appshots/pending/') && req.method === 'GET') {
            const id = pathname.slice('/api/appshots/pending/'.length)
            const item = pending.get(id)
            if (!item) return sendJson(res, 404, { ok: false, error: 'appshot expired' })
            const bytes = await readFile(item.imagePath)
            return sendJson(res, 200, {
              ok: true,
              id: item.id,
              owner: item.owner,
              title: item.title,
              text: item.text,
              textTruncated: item.textTruncated,
              axTrusted: item.axTrusted,
              mediaType: 'image/png',
              data: bytes.toString('base64'),
              name: `appshot-${item.owner || 'window'}.png`,
            })
          }
          if (pathname.startsWith('/api/appshots/pending/') && req.method === 'DELETE') {
            const id = pathname.slice('/api/appshots/pending/'.length)
            await forget(id)
            return sendJson(res, 200, { ok: true })
          }
          if (pathname === '/api/appshots/latest' && req.method === 'GET') {
            const latest = [...pending.values()].at(-1)
            if (!latest) {
              res.writeHead(204, { 'cache-control': 'no-store' })
              res.end()
              return
            }
            return sendJson(res, 200, { ok: true, id: latest.id, owner: latest.owner, title: latest.title })
          }
          if (pathname === '/api/appshots/events' && req.method === 'GET') {
            res.writeHead(200, {
              'content-type': 'text/event-stream; charset=utf-8',
              'cache-control': 'no-cache, no-transform',
              connection: 'keep-alive',
            })
            res.write(':ok\n\n')
            sseClients.add(res)
            req.on('close', () => { sseClients.delete(res) })
            return
          }
          return methodNotAllowed(res, ['GET', 'POST', 'DELETE'])
        } catch (error) {
          return sendJson(res, 400, { ok: false, error: error instanceof Error ? error.message : String(error) })
        }
      },
    })
    sctx.effect(() => dispose, 'dsh-appshots: routes')
  })

  void restorePending()
  void restoreHidden()
  ctx.on('agent/pre-step', async ({ agent, step, signal }, next) => {
    const decision = await next()
    if (decision.kind === 'reject' || signal.aborted || step !== 1) return decision
    const items = await takeHidden(agent.session.id)
    if (!items.length) return decision
    const text = renderHiddenText(items)
    if (!text) return decision
    return {
      ...decision,
      messages: [...decision.messages, createUserMessage({
        content: [{ type: 'text', text }],
        source: {
          kind: 'plugin',
          plugin: name,
          form: 'snapshot',
          sections: [{ name, text }],
        },
      })],
    }
  }, { prepend: true })
  startHotkey()
  ctx.effect(() => () => {
    if (hotkeyTimer) clearTimeout(hotkeyTimer)
    stopHotkey()
    for (const res of sseClients) {
      try { res.end() } catch {}
    }
    sseClients.clear()
    for (const item of pending.values()) clearTimeout(item.timer)
    pending.clear()
  }, 'dsh-appshots: lifecycle')
}
