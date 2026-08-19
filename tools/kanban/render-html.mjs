#!/usr/bin/env node
// Renders board.json into a self-contained board.html.
// Two jobs: (1) review the board before it is pushed to Trello, (2) fallback
// screenshot source if a headless capture of the real Trello board comes back empty.
//
//   node scripts/kanban/render-html.mjs

import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { loadBoard } from './board-data.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const board = loadBoard();

// Trello's label palette, so the preview and the real board read the same.
const LABEL_HEX = {
  green: '#4bce97',
  yellow: '#f5cd47',
  orange: '#fea362',
  red: '#f87168',
  purple: '#9f8fef',
  blue: '#579dff',
  sky: '#6cc3e0',
  lime: '#94c748',
  pink: '#e774bb',
  black: '#8590a2',
};

const colorOf = (name) => LABEL_HEX[board.labels.find((l) => l.name === name)?.color] ?? '#8590a2';

const esc = (s) =>
  String(s).replace(
    /[&<>"']/g,
    (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
  );

// "2026-08-01T15:58:00+03:00" -> "1 Aug 2026". Parsed off the string, not via Date,
// so the output never shifts with the machine's timezone.
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
function shortDate(iso) {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  if (!m) return iso;
  return `${Number(m[3])} ${MONTHS[Number(m[2]) - 1]} ${m[1]}`;
}

function dateBadge(card) {
  const { start, due } = card;
  if (!start && !due) return '';
  const text =
    start && due
      ? shortDate(start) === shortDate(due)
        ? shortDate(due)
        : `${shortDate(start)} → ${shortDate(due)}`
      : shortDate(due ?? start);
  const done = card.list === 'Done';
  return `<span class="badge ${done ? 'badge-done' : ''}">${esc(text)}</span>`;
}

function renderCard(card) {
  const labels = (card.labels ?? [])
    .map((n) => `<span class="label" style="background:${colorOf(n)}" title="${esc(n)}"></span>`)
    .join('');
  const checks = card.checklist?.length
    ? `<span class="badge">☑ ${card.checklist.length}</span>`
    : '';
  const desc = card.desc ? '<span class="badge">≡</span>' : '';
  const meta = [dateBadge(card), desc, checks].filter(Boolean).join('');
  return `<div class="card">
      ${labels ? `<div class="labels">${labels}</div>` : ''}
      <div class="title">${esc(card.name)}</div>
      ${meta ? `<div class="meta">${meta}</div>` : ''}
    </div>`;
}

const columns = board.lists
  .map((list) => {
    const cards = board.cards.filter((c) => c.list === list);
    return `<section class="list">
      <header><h2>${esc(list)}</h2><span class="count">${cards.length}</span></header>
      <div class="cards">${cards.map(renderCard).join('')}</div>
    </section>`;
  })
  .join('');

const total = board.cards.length;

const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>${esc(board.board.name)} — Kanban</title>
<style>
  * { box-sizing: border-box; }
  body {
    margin: 0; padding: 20px 16px 28px;
    font: 14px/1.45 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
    background: #1d2125; color: #b6c2cf; -webkit-font-smoothing: antialiased;
  }
  .board-head { padding: 0 6px 14px; }
  .board-head h1 { margin: 0; font-size: 19px; color: #e6edf3; font-weight: 600; letter-spacing: .2px; }
  .board-head p { margin: 5px 0 0; font-size: 12px; color: #8590a2; }
  .board { display: flex; gap: 12px; align-items: flex-start; }
  .list {
    flex: 1 1 0; min-width: 0;
    background: #101204; border-radius: 12px; padding: 10px 8px 12px;
    box-shadow: 0 1px 1px rgba(0,0,0,.3);
  }
  .list > header { display: flex; align-items: center; gap: 8px; padding: 2px 6px 10px; }
  .list h2 { margin: 0; font-size: 13px; font-weight: 600; color: #e6edf3; }
  .count {
    margin-left: auto; font-size: 11px; color: #8590a2;
    background: #22272b; border-radius: 10px; padding: 1px 8px;
  }
  .cards { display: flex; flex-direction: column; gap: 8px; }
  .card {
    background: #22272b; border-radius: 8px; padding: 8px 10px 9px;
    box-shadow: 0 1px 1px rgba(0,0,0,.25); border: 1px solid transparent;
  }
  .labels { display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 6px; }
  .label { width: 38px; height: 8px; border-radius: 4px; }
  .title { color: #e6edf3; font-size: 13px; overflow-wrap: break-word; }
  .meta { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 7px; }
  .badge {
    font-size: 11px; color: #8590a2; background: #2c3339;
    border-radius: 4px; padding: 1px 6px; white-space: nowrap;
  }
  .badge-done { background: #1f472e; color: #7ee2b8; }
</style>
</head>
<body>
  <div class="board-head">
    <h1>${esc(board.board.name)}</h1>
    <p>${total} cards · ${esc(board.lists.length)} lists · preview rendered from board.json</p>
  </div>
  <div class="board">${columns}</div>
</body>
</html>
`;

const out = join(here, 'board.html');
writeFileSync(out, html);

console.log(`wrote ${out}`);
for (const list of board.lists) {
  console.log(`  ${String(board.cards.filter((c) => c.list === list).length).padStart(3)}  ${list}`);
}
console.log(`  ${String(total).padStart(3)}  TOTAL`);
