// SPDX-License-Identifier: MIT

namespace SmartProbe;

/// <summary>
/// The page served at "/" in Development: one button per endpoint, the response pretty-printed,
/// and how long it took. Self-contained — no scripts or styles fetched from anywhere — so the
/// probe keeps zero dependencies and works on a machine with no internet.
/// </summary>
internal static class DevPage
{
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>SMARTProbe</title>
        <style>
          :root { color-scheme: light dark;
                  --bg: #ffffff; --fg: #1b1b1f; --muted: #6b6b76; --card: #f3f3f6; --line: #dcdce3;
                  --accent: #0b5fbf; --accent-fg: #ffffff; --ok: #1a7f37; --err: #b42318; }
          @media (prefers-color-scheme: dark) {
            :root { --bg: #121216; --fg: #e8e8ee; --muted: #9a9aa6; --card: #1c1c22; --line: #2c2c34;
                    --accent: #4f9cf9; --accent-fg: #0b0b0f; --ok: #3fb950; --err: #f85149; } }
          * { box-sizing: border-box; }
          body { margin: 0 auto; max-width: 960px; padding: 32px 16px 48px; background: var(--bg); color: var(--fg);
                 font: 15px/1.5 system-ui, "Segoe UI", sans-serif; }
          h1 { margin: 0 0 4px; font-size: 24px; letter-spacing: -0.01em; }
          h1 small { font-size: 13px; font-weight: 500; color: var(--muted); margin-left: 8px;
                     border: 1px solid var(--line); border-radius: 999px; padding: 1px 8px; vertical-align: middle; }
          p.lead { margin: 0 0 20px; color: var(--muted); }
          .row { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 14px; }
          button { font: inherit; font-weight: 600; padding: 8px 14px; border-radius: 8px; cursor: pointer;
                   border: 1px solid var(--line); background: var(--card); color: var(--fg); }
          button.primary { background: var(--accent); color: var(--accent-fg); border-color: transparent; }
          button:disabled { opacity: .55; cursor: progress; }
          #meta { display: flex; gap: 14px; flex-wrap: wrap; font-size: 13px; color: var(--muted); min-height: 20px; margin-bottom: 8px; }
          #meta .ok { color: var(--ok); font-weight: 600; } #meta .err { color: var(--err); font-weight: 600; }
          pre { margin: 0; padding: 14px 16px; background: var(--card); border: 1px solid var(--line); border-radius: 10px;
                overflow: auto; font: 13px/1.45 ui-monospace, Consolas, monospace; white-space: pre; min-height: 120px; }
          .k { color: var(--accent); } .s { color: var(--ok); } .n { color: #b3541e; } .b { color: #8250df; }
          @media (prefers-color-scheme: dark) { .n { color: #f0883e; } .b { color: #d2a8ff; } }
          footer { margin-top: 18px; font-size: 13px; color: var(--muted); }
          code { font: 12.5px ui-monospace, Consolas, monospace; background: var(--card); padding: 1px 5px; border-radius: 4px; }
        </style>
        </head>
        <body>
        <h1>SMARTProbe <small>development</small></h1>
        <p class="lead">Call an endpoint and see exactly what a client would receive.</p>
        <div class="row">
          <button class="primary" data-path="/probe/smart">GET /probe/smart</button>
          <button data-path="/probe/health">GET /probe/health</button>
        </div>
        <div id="meta"></div>
        <pre id="out">Choose an endpoint.</pre>
        <footer>This page exists only when <code>ASPNETCORE_ENVIRONMENT=Development</code>. In every other run, <code>/</code> returns a JSON index of routes.</footer>
        <script>
          const out = document.getElementById('out'), meta = document.getElementById('meta');
          const esc = s => s.replace(/[&<>]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));
          function highlight(json) {
            return esc(json).replace(/("(\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(true|false|null)\b|-?\d+(\.\d+)?([eE][+-]?\d+)?)/g, m => {
              let cls = 'n';
              if (m.startsWith('"')) cls = m.endsWith(':') ? 'k' : 's';
              else if (/true|false|null/.test(m)) cls = 'b';
              return '<span class="' + cls + '">' + m + '</span>';
            });
          }
          async function call(path, button) {
            document.querySelectorAll('button').forEach(b => b.disabled = true);
            meta.textContent = 'Requesting ' + path + ' …';
            const started = performance.now();
            try {
              const res = await fetch(path, { headers: { accept: 'application/json' } });
              const ms = Math.round(performance.now() - started);
              const text = await res.text();
              let body = text;
              try { body = JSON.stringify(JSON.parse(text), null, 2); } catch {}
              meta.innerHTML = '<span class="' + (res.ok ? 'ok' : 'err') + '">' + res.status + ' ' + esc(res.statusText) + '</span>'
                + '<span>' + ms + ' ms</span><span>' + text.length.toLocaleString() + ' bytes</span>'
                + '<span>' + esc(res.headers.get('content-type') || '') + '</span>';
              out.innerHTML = highlight(body);
            } catch (e) {
              meta.innerHTML = '<span class="err">Request failed</span>';
              out.textContent = String(e);
            } finally {
              document.querySelectorAll('button').forEach(b => b.disabled = false);
            }
          }
          document.querySelectorAll('button[data-path]').forEach(b => b.addEventListener('click', () => call(b.dataset.path, b)));
        </script>
        </body>
        </html>
        """;
}
