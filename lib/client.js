window.__ModuleLoader__.load({
  id: 'dsh-appshots',
  factory: (require) => {
    const module = { exports: {} }
    const exports = module.exports
    Object.defineProperty(exports, Symbol.toStringTag, { value: 'Module' })
    const React = require('react')

    const inject = ['slots', 'sessions', 'conversation']
    const RECENT_DEFAULT_MS = 60_000
    const CSS = `
.dsh-appshots-btn{
  appearance:none;border:0;background:transparent;color:var(--dsw-alias-label-secondary);
  width:32px;height:32px;border-radius:8px;display:inline-flex;align-items:center;justify-content:center;cursor:pointer;
}
.dsh-appshots-btn:hover{background:var(--dsw-alias-interactive-bg-hover);color:var(--dsw-alias-label-primary)}
.dsh-appshots-btn[data-busy="true"]{opacity:.55;cursor:progress}
.dsh-appshots-btn svg{width:18px;height:18px}
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

      async attachPending(id) {
        if (this.seen.has(id)) return
        this.seen.add(id)
        const run = async () => {
          try {
            const pending = await responseJson(await fetch(`/api/appshots/pending/${encodeURIComponent(id)}`))
            const sessionId = await this.resolveSession()
            const shell = await this.waitForShell(sessionId)
            const file = base64ToFile(pending.data, pending.name, pending.mediaType)
            const images = this.ctx.conversation.createDraftImages([file])
            if (!shell.addImages(images.map((image) => image.id))) {
              this.ctx.conversation.releaseDraftImages(images)
              throw new Error('当前输入框正忙，无法附加 Appshot')
            }
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
            try { shell.notify('info', `已附加 ${pending.owner || '窗口'} 的 Appshot`) } catch {}
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
    }

    exports.name = 'dsh-appshots'
    exports.apply = apply
    exports.inject = inject
    return module.exports
  },
})
