window.__ModuleLoader__.load({
  id: 'dsh-appshots',
  factory: (require) => {
    const module = { exports: {} }
    const exports = module.exports
    Object.defineProperty(exports, Symbol.toStringTag, { value: 'Module' })
    const React = require('react')

    const inject = ['slots', 'sessions', 'conversation']
    const RECENT_DEFAULT_MS = 60_000
    const PREVIEW_STORE_KEY = 'dsh-appshots-preview-v1'
    const PREVIEW_STORE_LIMIT = 40

    function readPreviewStore() {
      try {
        const raw = window.localStorage.getItem(PREVIEW_STORE_KEY)
        const parsed = raw ? JSON.parse(raw) : null
        return parsed && typeof parsed === 'object' ? parsed : {}
      } catch {
        return {}
      }
    }

    function writePreviewStore(store) {
      try { window.localStorage.setItem(PREVIEW_STORE_KEY, JSON.stringify(store)) } catch {}
    }

    function persistPreviewRecord(record) {
      const store = readPreviewStore()
      const keys = [record.name, record.title].filter(Boolean)
      for (const key of keys) store[key] = record
      const names = Object.keys(store)
      if (names.length > PREVIEW_STORE_LIMIT) {
        for (const key of names.slice(0, names.length - PREVIEW_STORE_LIMIT)) delete store[key]
      }
      writePreviewStore(store)
    }

    function storedPreviewForAlt(alt) {
      const store = readPreviewStore()
      const name = String(alt || '').trim()
      if (!name) return null
      const stem = name.replace(/^appshot-/, '').replace(/\.png$/i, '')
      return store[name] || store[stem] || null
    }
    const CSS = `
.dsh-appshots-btn{
  appearance:none;border:0;background:transparent;color:var(--dsw-alias-label-secondary);
  width:32px;height:32px;border-radius:8px;display:inline-flex;align-items:center;justify-content:center;cursor:pointer;
}
.dsh-appshots-btn:hover{background:var(--dsw-alias-interactive-bg-hover);color:var(--dsw-alias-label-primary)}
.dsh-appshots-btn[data-busy="true"]{opacity:.55;cursor:progress}
.dsh-appshots-btn svg{width:18px;height:18px}
.dsh-appshots-notice{
  box-sizing:border-box;
  width:100%;
  max-width:var(--dsh-composer-card-max-width);
  margin:0 auto 6px;
  padding:4px 8px;
  background:var(--dsw-alias-interactive-bg-hover);
  color:var(--dsw-alias-label-secondary);
  border-radius:8px;
  font-size:12px;
  line-height:18px;
}
.dsh-appshots-notice-wrap{
  box-sizing:border-box;
  width:100%;
  padding:0 var(--dsh-composer-side-clearance);
  display:flex;
  flex-direction:column;
  align-items:center;
}
.dsh-appshots-notice-item{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.dsh-appshots-lb-host{position:fixed;inset:0;z-index:1001;pointer-events:none}
.dsh-appshots-lb-tools{
  position:fixed;top:20px;right:68px;z-index:2;pointer-events:auto;
  display:flex;align-items:center;
}
.dsh-appshots-lb-textbtn{
  appearance:none;box-sizing:border-box;
  display:inline-flex;align-items:center;justify-content:center;
  height:36px;padding:0 14px;border-radius:999px;cursor:pointer;white-space:nowrap;
  border:1px solid var(--dsw-alias-border-l2-darkmode-thin);
  background:var(--dsw-specific-input-major);color:var(--dsw-alias-label-primary);
  font:var(--dsw-font-xs-13);
  font-family:var(--dsw-font-family);
}
.dsh-appshots-lb-textbtn:hover{background:var(--dsw-specific-input-major)}
.dsh-appshots-lb-textbtn[data-active="true"]{
  color:var(--dsw-alias-label-primary);
  background:var(--dsw-alias-button-ghost-active-fill);
  border-color:transparent;
  box-shadow:inset 0 0 0 1px var(--dsw-alias-button-ghost-active-border);
}
.dsh-appshots-lb-textbtn:focus-visible{outline:2px solid var(--dsw-alias-label-primary);outline-offset:2px}
.dsh-appshots-lb-card{
  position:fixed;z-index:1;pointer-events:auto;box-sizing:border-box;overflow:auto;
  border-radius:12px;padding:20px 24px 24px;
  background:var(--dsw-specific-input-major);color:var(--dsw-alias-label-primary);
  box-shadow:var(--dsw-shadow-lv3);overscroll-behavior:contain;
}
.dsh-appshots-lb-meta{
  margin:0 0 12px;
  font:var(--dsw-font-xxs-12);
  color:var(--dsw-alias-label-secondary);
}
.dsh-appshots-lb-body{
  margin:0;white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-word;user-select:text;
  font:var(--dsw-font-markdown-code-block);
  font-family:var(--ds-font-family-code, ui-monospace, SFMono-Regular, Menlo, monospace);
  color:var(--dsw-alias-label-primary);
}
`

    function errorMessage(error) {
      return error instanceof Error ? error.message : String(error)
    }

    async function responseJson(response) {
      if (response.status === 204) return { ok: true }
      const body = await response.json().catch(() => ({}))
      if (!response.ok || body.ok === false) throw new Error(body.error || `HTTP ${response.status}`)
      return body
    }

    function base64ToFile(data, name, mediaType) {
      const binary = atob(data)
      const bytes = new Uint8Array(binary.length)
      for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
      return new File([bytes], name || 'appshot.png', { type: mediaType || 'image/png' })
    }

    function sleep(ms) {
      return new Promise((resolve) => setTimeout(resolve, ms))
    }

    function CameraIcon() {
      return React.createElement('svg', { viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: '1.8' },
        React.createElement('path', { d: 'M4 8.5A2.5 2.5 0 0 1 6.5 6h1.2l.7-1.3A1.5 1.5 0 0 1 9.7 4h4.6a1.5 1.5 0 0 1 1.3.7L16.3 6h1.2A2.5 2.5 0 0 1 20 8.5v8A2.5 2.5 0 0 1 17.5 19h-11A2.5 2.5 0 0 1 4 16.5v-8Z' }),
        React.createElement('circle', { cx: '12', cy: '12.5', r: '3.2' }),
      )
    }

    class AppshotController {
      constructor(ctx) {
        this.ctx = ctx
        this.busy = false
        this.lastAttachAt = 0
        this.lastSessionId = undefined
        this.recentWindowMs = RECENT_DEFAULT_MS
        this.seen = new Set()
        this.listeners = new Set()
        this.disposeSource = undefined
        this.labelsByImageId = new Map()
        this.previewByImageId = new Map()
        this.previewByName = new Map()
      }

      rememberPreview(imageIds, pending) {
        const owner = String(pending.owner || '').trim() || '窗口'
        const title = String(pending.title || '').trim()
        const record = {
          owner,
          title,
          text: String(pending.text || ''),
          textTruncated: Boolean(pending.textTruncated),
          name: pending.name || `appshot-${owner}.png`,
        }
        for (const imageId of imageIds) this.previewByImageId.set(imageId, record)
        this.previewByName.set(record.name, record)
        if (title) this.previewByName.set(title, record)
        persistPreviewRecord(record)
      }

      previewForAlt(alt) {
        const name = String(alt || '').trim()
        if (!name) return null
        const stem = name.replace(/^appshot-/, '').replace(/\.png$/i, '')
        return this.previewByName.get(name)
          || this.previewByName.get(stem)
          || storedPreviewForAlt(name)
          || storedPreviewForAlt(stem)
          || null
      }

      formatPreview(record) {
        const title = record.title || 'Untitled'
        const owner = record.owner || 'Unknown'
        const body = String(record.text || '').trim() || '此窗口没有可用的辅助功能文本。'
        const truncated = record.textTruncated ? '\n(window text truncated)' : ''
        return {
          meta: `Window: "${title}", App: ${owner}.`,
          body: `${body}${truncated}`,
        }
      }

      subscribe(listener) {
        this.listeners.add(listener)
        return () => this.listeners.delete(listener)
      }

      publish() {
        for (const listener of this.listeners) listener()
      }

      async waitForShell(sessionId) {
        const started = Date.now()
        while (Date.now() - started < 4000) {
          try {
            return this.ctx.conversation.input.shell(sessionId)
          } catch {
            await sleep(40)
          }
        }
        throw new Error('会话输入框尚未就绪')
      }

      resolveTargetSessionId() {
        const current = this.ctx.sessions.list.getSnapshot().current
        if (current) return current
        const now = Date.now()
        if (this.lastAttachAt && this.lastSessionId && now - this.lastAttachAt < this.recentWindowMs) {
          return this.lastSessionId
        }
        return undefined
      }

      async resolveSession() {
        const sessions = this.ctx.sessions
        const target = this.resolveTargetSessionId()
        if (target) {
          sessions.open(target)
          this.lastSessionId = target
          return target
        }
        const created = await sessions.create({})
        sessions.open(created)
        this.lastSessionId = created
        return created
      }

      attachDrafts(sessionId, shell, file) {
        const conversation = this.ctx.conversation
        const drafts = typeof conversation.createDrafts === 'function'
          ? conversation.createDrafts(sessionId, [file])
          : conversation.createDraftImages([file])
        const ids = drafts.map((draft) => draft.id)
        const added = typeof shell.addAttachments === 'function'
          ? shell.addAttachments(ids)
          : shell.addImages(ids)
        if (!added) {
          if (typeof conversation.releaseDraftAttachments === 'function') conversation.releaseDraftAttachments(drafts)
          else conversation.releaseDraftImages(drafts)
          throw new Error('当前输入框正忙，无法附加 Appshot')
        }
        return ids
      }

      async attachPending(id) {
        if (this.seen.has(id)) return
        this.seen.add(id)
        const run = async () => {
          try {
            const pending = await responseJson(await fetch(`/api/appshots/pending/${encodeURIComponent(id)}`))
            const sessionId = await this.resolveSession()
            const shell = await this.waitForShell(sessionId)
            const file = base64ToFile(pending.data, pending.name, pending.mediaType)
            const imageIds = this.attachDrafts(sessionId, shell, file)
            const owner = String(pending.owner || '').trim() || '窗口'
            const title = String(pending.title || '').trim()
            const label = title && title !== owner ? `${owner} — ${title}` : owner
            for (const imageId of imageIds) this.labelsByImageId.set(imageId, label)
            this.rememberPreview(imageIds, pending)
            this.publish()
            if (pending.text) {
              const bind = await fetch('/api/appshots/bind', {
                method: 'POST',
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify({ sessionId, id }),
              })
              if (!bind.ok) console.warn('[dsh-appshots] bind failed', bind.status)
            }
            this.lastAttachAt = Date.now()
            this.lastSessionId = sessionId
            await fetch(`/api/appshots/pending/${encodeURIComponent(id)}`, { method: 'DELETE' }).catch(() => {})
          } catch (error) {
            this.seen.delete(id)
            throw error
          }
        }
        this.attaching = (this.attaching || Promise.resolve()).then(run, run)
        return this.attaching
      }

      async captureFromUi() {
        if (this.busy) return
        this.busy = true
        this.publish()
        try {
          const captured = await responseJson(await fetch('/api/appshots/capture', {
            method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: '{}',
          }))
          await this.attachPending(captured.id)
        } catch (error) {
          const current = this.ctx.sessions.list.getSnapshot().current
          if (current) {
            try { this.ctx.conversation.input.shell(current).notify('error', errorMessage(error)) } catch { console.error('[dsh-appshots]', error) }
          }
          console.error('[dsh-appshots]', error)
          throw error
        } finally {
          this.busy = false
          this.publish()
        }
      }

      async consumeLatest() {
        try {
          const listed = await responseJson(await fetch('/api/appshots/pending'))
          const items = Array.isArray(listed.items) ? listed.items : []
          for (const item of items) {
            if (item?.id) await this.attachPending(item.id)
          }
        } catch {
          try {
            const latest = await responseJson(await fetch('/api/appshots/latest'))
            if (latest?.id) await this.attachPending(latest.id)
          } catch {}
        }
      }

      start() {
        void this.refreshStatus()
        const source = new EventSource('/api/appshots/events')
        source.onmessage = (event) => {
          let payload
          try { payload = JSON.parse(event.data) } catch { return }
          if (payload?.type === 'captured' && payload.id) {
            void this.attachPending(payload.id).catch((error) => {
              console.error('[dsh-appshots]', error)
            })
          }
        }
        const poll = window.setInterval(() => { void this.consumeLatest() }, 900)
        this.disposeSource = () => {
          source.close()
          window.clearInterval(poll)
        }
        void this.consumeLatest()
        return () => {
          this.disposeSource?.()
        }
      }

      async refreshStatus() {
        try {
          const status = await responseJson(await fetch('/api/appshots/status'))
          if (typeof status.recentWindowMs === 'number') this.recentWindowMs = status.recentWindowMs
        } catch {}
      }
    }

    function mountLightboxOverlay(controller) {
      const host = document.createElement('div')
      host.dataset.plugin = 'dsh-appshots-lightbox'
      host.className = 'dsh-appshots-lb-host'
      document.body.appendChild(host)
      let open = false
      let record = null
      let imageEl = null
      let tools = null
      let button = null
      let card = null
      let metaEl = null
      let bodyEl = null

      const hideImage = (hidden) => {
        if (imageEl) imageEl.style.visibility = hidden ? 'hidden' : ''
      }

      const setImage = (next) => {
        if (imageEl && imageEl !== next) imageEl.style.visibility = ''
        imageEl = next
      }

      const ensureTools = () => {
        if (tools) return
        tools = document.createElement('div')
        tools.className = 'dsh-appshots-lb-tools'
        button = document.createElement('button')
        button.type = 'button'
        button.className = 'dsh-appshots-lb-textbtn'
        button.addEventListener('click', (event) => {
          event.preventDefault()
          event.stopPropagation()
          open = !open
          render()
        })
        tools.appendChild(button)
        host.appendChild(tools)
      }

      const ensureCard = () => {
        if (card) return
        card = document.createElement('div')
        card.className = 'dsh-appshots-lb-card'
        card.addEventListener('mousedown', (event) => event.stopPropagation())
        metaEl = document.createElement('p')
        metaEl.className = 'dsh-appshots-lb-meta'
        bodyEl = document.createElement('pre')
        bodyEl.className = 'dsh-appshots-lb-body'
        card.append(metaEl, bodyEl)
        host.appendChild(card)
      }

      const render = () => {
        if (!record) {
          hideImage(false)
          host.replaceChildren()
          tools = button = card = metaEl = bodyEl = null
          return
        }
        ensureTools()
        button.dataset.active = open ? 'true' : 'false'
        button.setAttribute('aria-pressed', open ? 'true' : 'false')
        button.textContent = open ? '查看图片' : '查看文本'
        if (!open || !imageEl) {
          hideImage(false)
          if (card) card.remove()
          card = metaEl = bodyEl = null
          return
        }
        ensureCard()
        const rect = imageEl.getBoundingClientRect()
        card.style.top = `${Math.round(rect.top)}px`
        card.style.left = `${Math.round(rect.left)}px`
        card.style.width = `${Math.max(1, Math.round(rect.width))}px`
        card.style.height = `${Math.max(1, Math.round(rect.height))}px`
        const preview = controller.formatPreview(record)
        if (metaEl.textContent !== preview.meta) metaEl.textContent = preview.meta
        if (bodyEl.textContent !== preview.body) bodyEl.textContent = preview.body
        hideImage(true)
      }

      const scan = () => {
        const dialogs = [...document.querySelectorAll('[role="dialog"][aria-modal="true"]')]
        const img = dialogs.map((dialog) => dialog.querySelector('img')).find(Boolean)
        const next = img ? controller.previewForAlt(img.getAttribute('alt') || '') : null
        if (!img) open = false
        setImage(img || null)
        record = next
        render()
      }

      const observer = new MutationObserver((mutations) => {
        if (mutations.every((mutation) => host.contains(mutation.target))) return
        scan()
      })
      observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['alt', 'src'] })
      window.addEventListener('resize', scan)
      const unsubscribe = controller.subscribe(scan)
      scan()
      return () => {
        hideImage(false)
        observer.disconnect()
        window.removeEventListener('resize', scan)
        unsubscribe()
        host.remove()
      }
    }

    function AppshotNotice({ controller, useInput }) {
      const [, setTick] = React.useState(0)
      React.useEffect(() => controller.subscribe(() => setTick((n) => n + 1)), [controller])
      const imageIds = useInput ? (useInput((s) => s.attachmentIds || s.imageIds) || []) : []
      const liveIds = new Set(Array.isArray(imageIds) ? imageIds : [])
      const labels = []
      const seen = new Set()
      let stale = false
      for (const [imageId, label] of [...controller.labelsByImageId]) {
        if (!liveIds.has(imageId)) {
          stale = true
          controller.labelsByImageId.delete(imageId)
          controller.previewByImageId.delete(imageId)
          continue
        }
        if (seen.has(label)) continue
        seen.add(label)
        labels.push(label)
      }
      React.useEffect(() => {
        if (stale) controller.publish()
      }, [stale])
      if (labels.length === 0) return null
      return React.createElement('div', { className: 'dsh-appshots-notice-wrap' },
        React.createElement('div', { className: 'dsh-appshots-notice', role: 'status' },
          labels.map((label) => React.createElement('div', {
            key: label,
            className: 'dsh-appshots-notice-item',
          }, `已附加 ${label} 的 Appshot`)),
        ),
      )
    }

    function AppshotButton({ controller }) {
      const [, setTick] = React.useState(0)
      React.useEffect(() => controller.subscribe(() => setTick((n) => n + 1)), [controller])
      return React.createElement('button', {
        type: 'button',
        className: 'dsh-appshots-btn',
        title: 'Appshot：捕获前台窗口',
        'data-busy': controller.busy ? 'true' : 'false',
        disabled: controller.busy,
        onClick: () => { void controller.captureFromUi() },
      }, React.createElement(CameraIcon))
    }

    function apply(ctx) {
      const controller = new AppshotController(ctx)
      ctx.effect(() => {
        const style = document.createElement('style')
        style.dataset.plugin = 'dsh-appshots'
        style.textContent = CSS
        document.head.appendChild(style)
        return () => style.remove()
      }, 'dsh-appshots: styles')
      ctx.effect(() => controller.start(), 'dsh-appshots: events')
      ctx.slots.inject('conversation.input.left', () => ctx.slots.register({
        name: 'conversation.input.left',
        id: 'appshots-button',
        order: 9600,
        label: 'Appshot',
      }, () => React.createElement(AppshotButton, { controller })))
      ctx.slots.inject('conversation.input.dock', () => ctx.slots.register({
        name: 'conversation.input.dock',
        id: 'appshots-notice',
        order: 50,
        label: 'Appshot',
      }, ({ useInput }) => React.createElement(AppshotNotice, { controller, useInput })))
      ctx.effect(() => mountLightboxOverlay(controller), 'dsh-appshots: lightbox')
    }

    exports.name = 'dsh-appshots'
    exports.apply = apply
    exports.inject = inject
    return module.exports
  },
})
