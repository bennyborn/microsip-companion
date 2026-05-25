using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MicroSIPRemote
{
    internal sealed class HttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CallStateMonitor _monitor;
        private Thread _thread;
        private bool _disposed;

        public HttpServer(int port, CallStateMonitor monitor)
        {
            _monitor = monitor;
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public void Start()
        {
            _listener.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "HttpServer" };
            _thread.Start();
        }

        private void Loop()
        {
            while (!_disposed)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => Handle(client));
                }
                catch (SocketException) when (_disposed) { break; }
                catch { }
            }
        }

        private void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    // Read until \r\n\r\n (end of HTTP headers)
                    var buf = new byte[8192];
                    int total = 0, headerEnd = -1;
                    while (total < buf.Length)
                    {
                        int n = stream.Read(buf, total, buf.Length - total);
                        if (n == 0) return;
                        total += n;
                        for (int i = 0; i <= total - 4; i++)
                        {
                            if (buf[i] == '\r' && buf[i+1] == '\n' &&
                                buf[i+2] == '\r' && buf[i+3] == '\n')
                            { headerEnd = i + 4; break; }
                        }
                        if (headerEnd >= 0) break;
                    }
                    if (headerEnd < 0) return;

                    var firstLine = Encoding.ASCII.GetString(buf, 0, headerEnd)
                        .Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                    var parts = firstLine.Split(' ');
                    if (parts.Length < 2) return;

                    var method = parts[0];
                    var rawPath = parts[1];
                    var q = rawPath.IndexOf('?');
                    var path = (q >= 0 ? rawPath.Substring(0, q) : rawPath).TrimEnd('/');
                    if (path == "") path = "/";

                    string body; string ct; int status = 200;

                    if (path == "/events" && method == "GET")
                    {
                        HandleSse(stream);
                        return;
                    }

                    if (path == "/" && method == "GET")
                    { body = WebUi(); ct = "text/html; charset=utf-8"; }
                    else if (path == "/state" && method == "GET")
                    { body = "{\"state\":\"" + _monitor.Current.ToString().ToLower() + "\"}"; ct = "application/json"; }
                    else if (path == "/hangupincoming" && method == "POST")
                    { MicroSipController.HangupIncoming(); body = "{\"ok\":true}"; ct = "application/json"; }
                    else if (path == "/hangupall" && method == "POST")
                    { MicroSipController.HangupAll(); body = "{\"ok\":true}"; ct = "application/json"; }
                    else if (path == "/answer" && method == "POST")
                    { MicroSipController.Answer(); body = "{\"ok\":true}"; ct = "application/json"; }
                    else if (path == "/debug" && method == "GET")
                    {
                        var info = _monitor.DebugInfo.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        body = "{\"state\":\"" + _monitor.Current.ToString().ToLower() + "\",\"debug\":\"" + info + "\"}";
                        ct = "application/json";
                    }
                    else if (path == "/ping")
                    { body = "{\"status\":\"ok\"}"; ct = "application/json"; }
                    else
                    { status = 404; body = "Not found"; ct = "text/plain"; }

                    var bodyBytes = Encoding.UTF8.GetBytes(body);
                    var hdr = "HTTP/1.1 " + status + " OK\r\n" +
                              "Content-Type: " + ct + "\r\n" +
                              "Content-Length: " + bodyBytes.Length + "\r\n" +
                              "Access-Control-Allow-Origin: *\r\n" +
                              "Connection: close\r\n\r\n";
                    var hdrBytes = Encoding.ASCII.GetBytes(hdr);
                    stream.Write(hdrBytes, 0, hdrBytes.Length);
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                }
            }
            catch { }
        }

        private void HandleSse(System.Net.Sockets.NetworkStream stream)
        {
            var headers = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/event-stream\r\n" +
                "Cache-Control: no-cache\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Connection: keep-alive\r\n\r\n");
            stream.Write(headers, 0, headers.Length);

            WriteSseData(stream, _monitor.Current);

            using var trigger = new System.Threading.AutoResetEvent(false);
            int pending = -1;

            Action<CallState> handler = s =>
            {
                System.Threading.Interlocked.Exchange(ref pending, (int)s);
                trigger.Set();
            };
            _monitor.StateChanged += handler;
            try
            {
                while (!_disposed)
                {
                    bool signaled = trigger.WaitOne(20_000);
                    if (_disposed) break;
                    if (signaled)
                    {
                        int s = System.Threading.Interlocked.Exchange(ref pending, -1);
                        if (s >= 0) WriteSseData(stream, (CallState)s);
                    }
                    else
                    {
                        var hb = Encoding.ASCII.GetBytes(": heartbeat\n\n");
                        stream.Write(hb, 0, hb.Length);
                    }
                }
            }
            catch { /* client disconnected */ }
            finally { _monitor.StateChanged -= handler; }
        }

        private static void WriteSseData(System.Net.Sockets.NetworkStream stream, CallState state)
        {
            var bytes = Encoding.UTF8.GetBytes("data: {\"state\":\"" + state.ToString().ToLower() + "\"}\n\n");
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string WebUi() => @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1,maximum-scale=1"">
<meta name=""apple-mobile-web-app-capable"" content=""yes"">
<title>MicroSIP Companion</title>
<style>
  :root {
    --bg: #0f0f13;
    --surface: #1c1c24;
    --border: #2e2e3a;
    --text: #e8e8f0;
    --muted: #6b6b80;
    --green: #22c55e;
    --green-dim: #15803d;
    --red: #ef4444;
    --red-dim: #991b1b;
    --pulse-green: rgba(34,197,94,0.35);
    --pulse-red: rgba(239,68,68,0.35);
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body {
    height: 100%; background: var(--bg); color: var(--text);
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
    display: flex; flex-direction: column; align-items: center; justify-content: center;
    user-select: none; -webkit-tap-highlight-color: transparent;
  }
  h1 { font-size: 1rem; font-weight: 500; color: var(--muted); letter-spacing: .05em;
       text-transform: uppercase; margin-bottom: 2.5rem; }
  #status {
    font-size: 1.1rem; margin-bottom: 2.5rem; min-height: 1.5em;
    display: flex; align-items: center; gap: .6rem;
  }
  .dot { width: 10px; height: 10px; border-radius: 50%; background: var(--muted); flex-shrink: 0; }
  .dot.incoming { background: var(--green); animation: pulse-green 1s infinite; }
  .dot.active   { background: var(--red);   animation: pulse-red   1s infinite; }
  @keyframes pulse-green {
    0%,100% { box-shadow: 0 0 0 0 var(--pulse-green); }
    50%      { box-shadow: 0 0 0 8px transparent; }
  }
  @keyframes pulse-red {
    0%,100% { box-shadow: 0 0 0 0 var(--pulse-red); }
    50%      { box-shadow: 0 0 0 8px transparent; }
  }
  #buttons { display: flex; flex-direction: column; gap: 1rem; width: min(88vw, 320px); }
  button {
    display: none; width: 100%; padding: 1.1rem 1.5rem;
    border: none; border-radius: 14px; font-size: 1.15rem; font-weight: 600;
    cursor: pointer; transition: opacity .15s, transform .1s;
    -webkit-tap-highlight-color: transparent;
  }
  button:active { opacity: .8; transform: scale(.97); }
  #btn-answer { background: var(--green); color: #fff; }
  #btn-hangup { background: var(--red);   color: #fff; }
  button.visible { display: block; }
  #idle-msg {
    display: none; color: var(--muted); font-size: .95rem;
    text-align: center; line-height: 1.6;
  }
  #idle-msg.visible { display: block; }
  #error { display: none; color: var(--red); font-size: .85rem; margin-top: 1.5rem; }
  #error.visible { display: block; }
</style>
</head>
<body>
<h1>MicroSIP Companion</h1>
<div id=""status""><span class=""dot"" id=""dot""></span><span id=""status-text"">Connecting…</span></div>
<div id=""buttons"">
  <button id=""btn-answer"">&#128222; Answer</button>
  <button id=""btn-hangup"">&#128245; Hang Up</button>
</div>
<div id=""idle-msg"">No active call.</div>
<div id=""error""></div>
<script>
(function(){
  var state = null;
  var dot = document.getElementById('dot');
  var statusText = document.getElementById('status-text');
  var btnAnswer = document.getElementById('btn-answer');
  var btnHangup = document.getElementById('btn-hangup');
  var idleMsg = document.getElementById('idle-msg');
  var errEl = document.getElementById('error');

  function render(s) {
    dot.className = 'dot ' + (s === 'idle' ? '' : s);
    var labels = { idle: 'Ready', incoming: 'Incoming Call', active: 'Call Active' };
    statusText.textContent = labels[s] || s;
    btnAnswer.className = s === 'incoming' ? 'visible' : '';
    btnHangup.className = (s === 'incoming' || s === 'active') ? 'visible' : '';
    idleMsg.className = s === 'idle' ? 'visible' : '';
  }

  function post(endpoint) {
    fetch(endpoint, { method: 'POST' }).catch(function(){});
  }

  btnAnswer.addEventListener('click', function(){ post('/answer'); });
  btnHangup.addEventListener('click', function(){
    post(state === 'incoming' ? '/hangupincoming' : '/hangupall');
  });

  var es = new EventSource('/events');
  es.onmessage = function(e) {
    try {
      var d = JSON.parse(e.data);
      if (d.state !== state) { state = d.state; render(state); }
      errEl.className = '';
    } catch(ex) {}
  };
  es.onerror = function() {
    errEl.className = 'visible';
    errEl.textContent = 'Connection lost…';
  };
})();
</script>
</body>
</html>";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _listener.Stop(); } catch { }
        }
    }
}
