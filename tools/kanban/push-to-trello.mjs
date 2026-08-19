#!/usr/bin/env node
// Publishes board.json to Trello via the REST API. No dependencies.
//
//   node scripts/kanban/push-to-trello.mjs              # dry run (default, no network)
//   TRELLO_KEY=... TRELLO_TOKEN=... node scripts/kanban/push-to-trello.mjs --push
//
// Credentials come from the environment only. Never put a token in this file,
// in board.json, or anywhere else that gets committed — it grants full
// read/write access to the whole Trello account. Revoke it once the board is up:
// https://trello.com/my/account -> Applications.

import { loadBoard, validate } from './board-data.mjs';

const board = loadBoard();

const PUSH = process.argv.includes('--push');
const KEY = process.env.TRELLO_KEY;
const TOKEN = process.env.TRELLO_TOKEN;

// Trello allows 300 req/10s per key but only 100 req/10s per token, so the token
// is the real ceiling: 10/s. 130ms keeps a margin without making the run drag.
const THROTTLE_MS = 130;
const API = 'https://api.trello.com/1';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** POST/DELETE against the Trello API, with one retry on a rate-limit reply. */
async function call(method, path, params = {}) {
  const url = new URL(API + path);
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null) url.searchParams.set(k, String(v));
  }
  url.searchParams.set('key', KEY);
  url.searchParams.set('token', TOKEN);

  for (let attempt = 0; attempt < 4; attempt++) {
    const res = await fetch(url, { method, headers: { Accept: 'application/json' } });
    if (res.status === 429) {
      const wait = 2000 * (attempt + 1);
      console.warn(`  rate limited, waiting ${wait}ms…`);
      await sleep(wait);
      continue;
    }
    const body = await res.text();
    if (!res.ok) {
      // Never echo the query string back — it carries the token.
      throw new Error(`${method} ${path} → ${res.status} ${res.statusText}\n${body.slice(0, 400)}`);
    }
    await sleep(THROTTLE_MS);
    return body ? JSON.parse(body) : null;
  }
  throw new Error(`${method} ${path} → gave up after repeated 429s`);
}

/** Cards whose work is finished get a ticked due date, so the badge reads green. */
const isFinished = (card) => card.list === 'Done' || card.list === 'Cancelled / Parked';

function dryRun() {
  console.log(`\n${board.board.name}\n${'='.repeat(board.board.name.length)}\n`);
  for (const list of board.lists) {
    const cards = board.cards.filter((c) => c.list === list);
    console.log(`${list}  (${cards.length})`);
    for (const c of cards) {
      const when = c.start ?? c.due;
      const date = when ? when.slice(0, 10) : '          ';
      const checks = c.checklist?.length ? ` [${c.checklist.length}]` : '';
      const labels = c.labels?.length ? `  ·  ${c.labels.join(', ')}` : '';
      console.log(`   ${date}  ${c.name}${checks}${labels}`);
    }
    console.log('');
  }

  // Requests = board + default-label cleanup + labels + lists + cards + checklists + items.
  const checklists = board.cards.filter((c) => c.checklist?.length);
  const items = checklists.reduce((n, c) => n + c.checklist.length, 0);
  const requests =
    1 + 6 + board.labels.length + board.lists.length + board.cards.length + checklists.length + items;
  console.log(`${board.cards.length} cards · ${checklists.length} checklists · ${items} items`);
  console.log(`~${requests} API requests, ~${Math.ceil((requests * THROTTLE_MS) / 1000)}s to push`);
  console.log('\nDry run only. Re-run with --push (and TRELLO_KEY / TRELLO_TOKEN set) to publish.\n');
}

async function push() {
  console.log(`Creating board "${board.board.name}"…`);
  const created = await call('POST', '/boards/', {
    name: board.board.name,
    desc: board.board.desc,
    defaultLists: false, // we create our own, in our own order
    prefs_background: board.board.prefs_background ?? 'blue',
  });
  console.log(`  ${created.shortUrl}`);

  // A new board ships with six unnamed default labels. Remove them so the
  // label picker only holds ours.
  const existing = await call('GET', `/boards/${created.id}/labels`);
  for (const label of existing) {
    await call('DELETE', `/labels/${label.id}`);
  }

  console.log(`Creating ${board.labels.length} labels…`);
  const labelIds = {};
  for (const label of board.labels) {
    const made = await call('POST', '/labels', {
      idBoard: created.id,
      name: label.name,
      color: label.color,
    });
    labelIds[label.name] = made.id;
  }

  // Lists and cards are positional: create them sequentially or the order scrambles.
  console.log(`Creating ${board.lists.length} lists…`);
  const listIds = {};
  for (const name of board.lists) {
    const made = await call('POST', '/lists', { name, idBoard: created.id, pos: 'bottom' });
    listIds[name] = made.id;
  }

  console.log(`Creating ${board.cards.length} cards…`);
  for (const [i, card] of board.cards.entries()) {
    const made = await call('POST', '/cards', {
      idList: listIds[card.list],
      name: card.name,
      desc: card.desc,
      start: card.start,
      due: card.due,
      dueComplete: card.due && isFinished(card) ? 'true' : undefined,
      idLabels: (card.labels ?? []).map((n) => labelIds[n]).join(','),
      pos: 'bottom',
    });

    if (card.checklist?.length) {
      const checklist = await call('POST', '/checklists', { idCard: made.id, name: 'History' });
      for (const item of card.checklist) {
        await call('POST', `/checklists/${checklist.id}/checkItems`, {
          name: item,
          checked: isFinished(card) ? 'true' : 'false',
          pos: 'bottom',
        });
      }
    }
    console.log(`  ${String(i + 1).padStart(2)}/${board.cards.length}  ${card.name}`);
  }

  console.log(`\nDone.\n\n  ${created.shortUrl}\n`);
  console.log('Next: open the board, check it, then Share → make the link public so the');
  console.log('screenshot can be captured. Revoke the API token when finished.\n');
}

const problems = validate(board);
if (problems.length) {
  console.error('board.json has problems:\n' + problems.map((p) => `  - ${p}`).join('\n'));
  process.exit(1);
}

if (!PUSH) {
  dryRun();
} else if (!KEY || !TOKEN) {
  console.error('Set TRELLO_KEY and TRELLO_TOKEN in the environment to push.');
  process.exit(1);
} else {
  await push();
}
