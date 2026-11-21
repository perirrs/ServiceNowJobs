import assert from "node:assert/strict";
import { test } from "node:test";
import { GraphChat } from "../src/graph.js";

/**
 * Teams lists a chat's messages by when they were last changed, newest first. A message edited
 * in place (the pinned tracker) sits on top with an old creation time; what was posted before
 * that edit comes after it, and must still be read.
 */
const page = {
  value: [
    { id: "tracker", messageType: "message", createdDateTime: "2026-11-21T09:48:48Z", lastModifiedDateTime: "2026-11-21T09:57:50Z", from: { user: { id: "u-m" } }, body: { content: "Team tracker" } },
    { id: "reply", messageType: "message", createdDateTime: "2026-11-21T09:54:43Z", lastModifiedDateTime: "2026-11-21T09:54:43Z", from: { user: { id: "u-j" } }, body: { content: "sure, on it" } },
    { id: "ask", messageType: "message", createdDateTime: "2026-11-21T09:51:07Z", lastModifiedDateTime: "2026-11-21T09:51:07Z", from: { user: { id: "u-s" } }, body: { content: "make the website live today" } },
    { id: "old", messageType: "message", createdDateTime: "2026-11-21T09:49:39Z", lastModifiedDateTime: "2026-11-21T09:49:39Z", from: { user: { id: "u-j" } }, body: { content: "will update Monday" } },
  ],
};

function client(): GraphChat {
  const g = new GraphChat("client", "tenant", ":memory:");
  (g as unknown as { call: () => Promise<unknown> }).call = async () => page;
  return g;
}

test("messages posted before an older message was edited are still read", async () => {
  const lines = await client().messagesSince("chat", "2026-11-21T09:51:00Z");
  assert.deepEqual(
    lines.map((l) => l.id),
    ["ask", "reply"],
  );
});

test("a chat's history since a time includes what sits below an edited message", async () => {
  const lines = await client().history("chat", 500, "2026-11-21T09:51:00Z");
  assert.deepEqual(
    lines.map((l) => l.id),
    ["ask", "reply"],
  );
});
