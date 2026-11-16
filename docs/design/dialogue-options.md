# Dialogue options (A-129 row 3, design note)

Status: proposal. Nothing here is built yet. Owners: folk lane (data, choosing, the reply), player lane (the talk window,
input, what an option does to the player's things).

## Today

- The talk window (Interact/FolkTalk, player lane) lists `FolkConversation.Topics`; a pick calls `FolkConversation.Ask`,
  which returns one `Answer` (text, gesture, offer, ends).
- The only choice after an answer is Accept/Decline, built in FolkTalk when the answer offers a quest or an item.
- Lines live in `Assets/StreamingAssets/dialogue.json` (`conversation`, `beastTalk`), read live (LiveJsonFile).

## What options add

An answer can carry 1-4 player replies. Picking one shows the folk's answer to that reply, which can carry its own
replies (a short branch). Accept/Decline becomes two ordinary options, so there's one choice system, not two (rule 33).

## Data shape (dialogue.json)

A line gains an optional `options` list. Each option has the reply's words and what happens:

```json
{
  "text": "Wolves took two of my goats. I'd pay to see the pack moved on.",
  "need": true,
  "offer": "quest",
  "options": [
    { "say": "I'll deal with them.", "do": "accept", "then": "thanks" },
    { "say": "How much?", "then": "haggle", "trust": "low" },
    { "say": "Not today.", "do": "decline" }
  ]
}
```

- `say`: the player's words (slots allowed, same as lines).
- `then`: a topic id whose lines answer it (any id, including new ones that never show in the topic list: a topic with
  no label in `topics` is reply-only). Left out: back to the topic list.
- `do`: one of a fixed list: `accept`, `decline` (the line's offer), `end`, `gesture:<emote>`. No free script.
- Any line key (traits, state, stance, mood, kin...) on an option is a condition on showing it, matched as lines are.
- The parse names bad options like any other entry (unknown `do`, `then` with no lines, more than 4, no `say`).

## Code shape

- `FolkConversation.Answer` gains `Option[] Options` (`Say`, `Then`, `Do`); `Ask` fills it from the chosen line,
  filtering by the same matcher. A line with an offer and no options gets the default Accept/Decline pair from code, so
  every old line keeps working.
- New `FolkConversation.Choose(in FolkTalker who, in Answer shown, int option)` returns the next Answer (asks `Then`).
- Depth cap 3 replies per branch; a loop back to a topic already asked in this talk is allowed (the pestered rules hold).

## Player lane (rule 11 ask: wire needs)

1. FolkTalk shows `Answer.Options` as its choice list (in place of the built-in Accept/Decline), keys 1-4, and calls
   `FolkConversation.Choose` on a pick.
2. `do: accept/decline` stays FolkTalk's (it already takes quests and gifts through Items); `end` closes the window;
   `gesture` uses the existing `Request.Gesture` send.
3. Network: none new. Words stay per machine; only what the folk does goes to the host over the existing `FolkTalkRpc`
   (Hold, Release, Gesture). A future `do` that changes host state (pay, give an item to the folk) would need a new
   Request value and a host-side check: that's a separate ask when such a `do` is carded.
4. Animal players: no options (no window), unchanged.

## Not in this card

- Persuasion rolls, prices, reputation changes (households/ownership waits on P-35).
- Voice, portraits, a dialogue editor tool.
