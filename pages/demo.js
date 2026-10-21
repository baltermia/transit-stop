// Static demo for GitHub Pages. Inlined before the UI's own script, it stands in for the HTTP API:
// the message list comes from a snapshot of TransitStop.Demo (window.transitStopDemo, inlined by
// build.sh), and publish/send only pretend, so the whole UI can be explored without a running app.
// Nothing is fetched, so the page also works when opened straight from disk.
(() => {
  "use strict";

  const repo = "https://github.com/baltermia/transit-stop";
  const realFetch = window.fetch.bind(window);
  const catalog = window.transitStopDemo;

  const json = body => new Response(JSON.stringify(body), { headers: { "Content-Type": "application/json" } });
  const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

  window.fetch = async (input, init) => {
    if (input === "api/messages")
      return json(catalog);
    if (input === "api/publish" || input === "api/send")
      return dispatch(input === "api/send", JSON.parse(init.body));
    return realFetch(input, init);
  };

  async function dispatch(send, request) {
    await delay(120 + Math.random() * 200);

    // a bare name means a queue, the same as the real API
    const raw = (request.destination || "").trim();
    const destination = send ? (raw.includes(":") ? raw : "queue:" + raw) : null;

    const m = catalog.messages.find(x => x.id === request.messageType);
    const name = m ? m.name : request.messageType;
    notice(send
      ? `Demo only: nothing was sent to ${destination}.`
      : m && m.consumers.length
        ? `Demo only: nothing was published. In the real app, ${m.consumers.join(", ")} would receive ${name}.`
        : `Demo only: nothing was published. No consumer in the demo app handles ${name}.`);

    return json({
      ok: true,
      mode: send ? "send" : "publish",
      messageType: request.messageType,
      destination,
      elapsedMs: Math.round((0.6 + Math.random() * 3) * 10) / 10,
    });
  }

  let noticeTimer;
  function notice(text) {
    const toast = document.getElementById("toast");
    toast.textContent = text;
    toast.classList.add("show");
    clearTimeout(noticeTimer);
    noticeTimer = setTimeout(() => toast.classList.remove("show"), 5000);
  }

  // ---- banner ----
  const style = document.createElement("style");
  style.textContent = `
    .demo-banner {
      display: flex; flex-wrap: wrap; align-items: center; justify-content: center; gap: 4px 12px;
      padding: 7px 16px; background: var(--accent-soft); color: var(--text);
      border-bottom: 1px solid var(--border); font-size: 13px; text-align: center;
    }
    .demo-banner b { color: var(--accent-strong); }
    .demo-banner a { color: var(--accent-strong); font-weight: 600; }
  `;
  document.head.append(style);

  const banner = document.createElement("div");
  banner.className = "demo-banner";
  banner.innerHTML = `<span><b>Static demo</b> of the TransitStop.Demo app. There is no backend here, so publishing and sending do nothing.</span>`
    + `<a href="${repo}" target="_blank" rel="noopener">Get TransitStop on GitHub →</a>`;
  document.body.prepend(banner);
})();
