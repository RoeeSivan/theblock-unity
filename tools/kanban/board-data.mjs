// Shared loader for board.json: validates it and puts the finished columns in
// chronological order. Used by both render-html.mjs and push-to-trello.mjs so the
// preview and the published board can never disagree.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

// Columns holding work that already happened read best oldest-first. The forward-
// looking columns keep the order they were authored in, which is priority order.
const CHRONOLOGICAL = new Set(['Done', 'Cancelled / Parked']);

/** When the work started. Undated cards sort last. */
const startedAt = (card) => Date.parse(card.start ?? card.due ?? '') || Number.MAX_SAFE_INTEGER;

export function loadBoard() {
  const board = JSON.parse(readFileSync(join(here, 'board.json'), 'utf8'));

  board.cards = board.lists.flatMap((list) => {
    const cards = board.cards.filter((c) => c.list === list);
    return CHRONOLOGICAL.has(list) ? cards.sort((a, b) => startedAt(a) - startedAt(b)) : cards;
  });

  return board;
}

/** Returns a list of human-readable problems; empty means the file is sound. */
export function validate(board) {
  const problems = [];
  const listNames = new Set(board.lists);
  const labelNames = new Set(board.labels.map((l) => l.name));

  for (const card of board.cards) {
    if (!listNames.has(card.list)) problems.push(`"${card.name}" → unknown list "${card.list}"`);
    for (const l of card.labels ?? []) {
      if (!labelNames.has(l)) problems.push(`"${card.name}" → unknown label "${l}"`);
    }
    for (const key of ['start', 'due']) {
      if (card[key] && Number.isNaN(Date.parse(card[key]))) {
        problems.push(`"${card.name}" → unparseable ${key} "${card[key]}"`);
      }
    }
    if (card.start && card.due && Date.parse(card.start) > Date.parse(card.due)) {
      problems.push(`"${card.name}" → start is after due`);
    }
  }
  return problems;
}
