#!/usr/bin/env node
// Screenshots a public Trello board into a single full-height PNG.
//
//   node scripts/kanban/capture-board.mjs <board-url> [out.png]
//
// Trello is a lazy-rendering SPA behind a marketing header, a cookie banner and a
// first-visit "About this board" dialog, and its lists scroll independently — so
// `chrome --screenshot` alone gives you a dimmed, cropped board. This drives Chrome
// over the DevTools Protocol instead: strip the chrome, size the viewport to the
// board's own bounding box, then capture beyond the viewport.
//
// Dependency-free: Chrome ships with macOS-installed Chrome, and Node 22 has a
// global WebSocket. The board must be publicly visible while this runs.

import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const PORT = 9222;

const args = process.argv.slice(2);
// --screen captures the board the way it actually looks on a monitor (lists scroll
// internally, standard aspect ratio) — the presentable shot. The default expands
// every list so all cards land in one tall image — the complete record.
const SCREEN = args.includes('--screen');
const [url, out = SCREEN ? 'kanban-board.png' : 'kanban-final.png'] = args.filter(
  (a) => !a.startsWith('--'),
);
if (!url) {
  console.error('usage: node scripts/kanban/capture-board.mjs <board-url> [out.png] [--screen]');
  process.exit(1);
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Everything that sits on top of, or beside, the board itself.
const CLEANUP = `
  (() => {
    const EXPAND_LISTS = ${!SCREEN};
    const kill = (sel) => document.querySelectorAll(sel).forEach((el) => el.remove());

    // Cookie consent, the marketing header shown to logged-out visitors, and the
    // first-visit board dialog. Trello's own test ids, so these are stable.
    kill('#cookies-consent-banner');
    kill('#banners');
    kill('[data-testid="spotlight--dialog"]');
    kill('[data-testid="jira-datasource-modal"]');
    kill('[role="dialog"]');
    document.querySelectorAll('[data-testid="logged-out-header-wide-tab"]')
      .forEach((el) => el.closest('header, nav, div[class*="header"]')?.remove());

    // Anything left pinned over the page (login CTAs, toasts).
    for (const el of document.querySelectorAll('body *')) {
      const s = getComputedStyle(el);
      if (s.position !== 'fixed' && s.position !== 'sticky') continue;
      const r = el.getBoundingClientRect();
      if (r.width > window.innerWidth * 0.6 && r.height > 24) el.remove();
    }

    if (EXPAND_LISTS) {
      // Let every list grow to its natural height instead of scrolling internally,
      // so all cards land in one image.
      for (const el of document.querySelectorAll('[data-testid="list-cards"], [data-testid="list"], [data-testid="list-wrapper"]')) {
        el.style.maxHeight = 'none';
        el.style.height = 'auto';
        el.style.overflow = 'visible';
      }
    }
    return document.querySelectorAll('[data-testid="list-card"]').length;
  })()
`;

// Union of the list columns, padded — the board proper, minus whatever surrounds it.
const MEASURE = `
  (() => {
    const lists = [...document.querySelectorAll('[data-testid="list-wrapper"]')];
    if (!lists.length) return null;
    const pad = 24;
    const boxes = lists.map((el) => el.getBoundingClientRect());
    const left = Math.min(...boxes.map((b) => b.left)) + scrollX;
    const top = Math.min(...boxes.map((b) => b.top)) + scrollY;
    const right = Math.max(...boxes.map((b) => b.right)) + scrollX;
    const bottom = Math.max(...boxes.map((b) => b.bottom)) + scrollY;
    return {
      x: Math.max(0, left - pad),
      y: Math.max(0, top - pad),
      width: right - left + pad * 2,
      height: bottom - top + pad * 2,
      lists: lists.length,
    };
  })()
`;

/** Minimal CDP client over the DevTools WebSocket. */
class Cdp {
  #ws;
  #id = 0;
  #pending = new Map();

  static async attach(wsUrl) {
    const cdp = new Cdp();
    cdp.#ws = new WebSocket(wsUrl);
    cdp.#ws.addEventListener('message', (ev) => {
      const msg = JSON.parse(ev.data);
      const waiter = cdp.#pending.get(msg.id);
      if (!waiter) return;
      cdp.#pending.delete(msg.id);
      msg.error ? waiter.reject(new Error(msg.error.message)) : waiter.resolve(msg.result);
    });
    await new Promise((resolve, reject) => {
      cdp.#ws.addEventListener('open', resolve, { once: true });
      cdp.#ws.addEventListener('error', () => reject(new Error('CDP socket failed')), {
        once: true,
      });
    });
    return cdp;
  }

  send(method, params = {}) {
    const id = ++this.#id;
    return new Promise((resolve, reject) => {
      this.#pending.set(id, { resolve, reject });
      this.#ws.send(JSON.stringify({ id, method, params }));
    });
  }

  /** Runs an expression in the page and returns its value. */
  async eval(expression) {
    const { result, exceptionDetails } = await this.send('Runtime.evaluate', {
      expression,
      returnByValue: true,
      awaitPromise: true,
    });
    if (exceptionDetails) throw new Error(exceptionDetails.text ?? 'page threw');
    return result.value;
  }

  close() {
    this.#ws.close();
  }
}

const chrome = spawn(
  CHROME,
  [
    '--headless=new',
    '--disable-gpu',
    '--hide-scrollbars',
    '--no-first-run',
    '--window-size=2560,2000',
    `--remote-debugging-port=${PORT}`,
    '--user-data-dir=/tmp/kanban-capture-profile',
    'about:blank',
  ],
  { stdio: 'ignore' },
);

let cdp;
try {
  // Wait for the debugging endpoint to answer.
  let target;
  for (let i = 0; i < 50; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${PORT}/json/list`);
      const targets = await res.json();
      target = targets.find((t) => t.type === 'page');
      if (target) break;
    } catch {
      /* not up yet */
    }
    await sleep(200);
  }
  if (!target) throw new Error('Chrome never exposed a debugging target');

  cdp = await Cdp.attach(target.webSocketDebuggerUrl);
  await cdp.send('Page.enable');
  await cdp.send('Runtime.enable');

  console.log(`loading ${url}`);
  await cdp.send('Emulation.setDeviceMetricsOverride', {
    width: 2560,
    height: 2000,
    deviceScaleFactor: 2, // retina-sharp text in the final image
    mobile: false,
  });
  await cdp.send('Page.navigate', { url });

  // Trello streams the board in after load; give it room, then strip the overlays.
  await sleep(9000);
  const cards = await cdp.eval(CLEANUP);
  console.log(`${cards} cards rendered`);

  // Re-run cleanup after a beat — Trello re-mounts some overlays post-hydration.
  await sleep(2500);
  await cdp.eval(CLEANUP);

  let box = await cdp.eval(MEASURE);
  if (!box) throw new Error('no lists found — is the board public?');
  console.log(`board is ${Math.round(box.width)}×${Math.round(box.height)} across ${box.lists} lists`);

  if (SCREEN) {
    // Board-as-it-looks-on-screen: lists keep scrolling internally, clipped to the
    // columns' width at a normal landscape height.
    box = { ...box, height: Math.round(box.width * 0.62) };
    await cdp.send('Emulation.setDeviceMetricsOverride', {
      width: Math.ceil(box.x + box.width),
      height: Math.ceil(box.y + box.height),
      deviceScaleFactor: 2,
      mobile: false,
    });
    await sleep(2500);
    await cdp.eval(CLEANUP);
  } else {
    // Grow the viewport past the board so every list renders in full. Two passes:
    // expanding the viewport lets more cards hydrate, which changes the height again.
    for (let pass = 0; pass < 2; pass++) {
      await cdp.send('Emulation.setDeviceMetricsOverride', {
        width: Math.ceil(box.x + box.width),
        height: Math.ceil(box.y + box.height) + 200,
        deviceScaleFactor: 2,
        mobile: false,
      });
      await sleep(3000);
      await cdp.eval(CLEANUP);
      const grown = await cdp.eval(MEASURE);
      const settled = Math.abs(grown.height - box.height) < 2;
      box = grown;
      if (settled) break;
      console.log(`  board grew to ${Math.round(box.width)}×${Math.round(box.height)}, re-measuring`);
    }
  }

  // No captureBeyondViewport: the viewport already covers the board, and asking
  // Chrome to shoot past it tiles the content — the board comes out duplicated.
  const { data } = await cdp.send('Page.captureScreenshot', {
    format: 'png',
    clip: { x: box.x, y: box.y, width: box.width, height: box.height, scale: 1 },
  });

  mkdirSync(dirname(out), { recursive: true });
  writeFileSync(out, Buffer.from(data, 'base64'));
  console.log(`wrote ${out}`);
} finally {
  cdp?.close();
  chrome.kill();
}
