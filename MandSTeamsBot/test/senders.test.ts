import assert from "node:assert/strict";
import { test } from "node:test";
import path from "node:path";
import { extraSenders, followedElsewhereFor, inheritSettings, othersGroupsFor, ReplyRouter, routeAdminMessage, senderDataDir, senderKey } from "../src/senders.js";

const SUJITH = "sujith@mergencorp.com";
const MAHANTH = "mahanth.m@mergencorp.com";
const senders = [
  { email: SUJITH, key: "sujith" },
  { email: MAHANTH, key: "mahanth" },
];

test("an extra sender's own messages go to their own sender", () => {
  assert.deepEqual(routeAdminMessage("list", MAHANTH, senders), { sender: MAHANTH, text: "list" });
});

test("everyone else goes to the main sender", () => {
  assert.deepEqual(routeAdminMessage("list", SUJITH, senders), { sender: SUJITH, text: "list" });
  assert.deepEqual(routeAdminMessage("all done", "priya@mergencorp.com", senders), { sender: SUJITH, text: "all done" });
});

test("a leading name sends the message to that sender instead, for admins only", () => {
  assert.deepEqual(routeAdminMessage("Mahanth: add group Sales", SUJITH, senders, true), { sender: MAHANTH, text: "add group Sales" });
  assert.deepEqual(routeAdminMessage("sujith, board", MAHANTH, senders, true), { sender: SUJITH, text: "board" });
  // Not an admin: the name is just part of what they said.
  assert.deepEqual(routeAdminMessage("Mahanth: hello", "priya@mergencorp.com", senders, false), { sender: SUJITH, text: "Mahanth: hello" });
  // A name that is not a sender is left alone.
  assert.deepEqual(routeAdminMessage("Madan: call me", SUJITH, senders, true), { sender: SUJITH, text: "Madan: call me" });
});

test("after talking to the other assistant, typed replies go there for 30 minutes; commands and buttons stay with your own", () => {
  const router = new ReplyRouter(senders);
  const at = (min: number) => Date.parse("2026-11-21T11:00:00Z") + min * 60_000;
  const typed = (text: string, min: number, from = SUJITH) => router.route(text, from, true, true, at(min)).sender;

  // Nothing said to Mahanth's assistant yet: everything is Sujith's.
  assert.equal(typed("who replied today?", 0), SUJITH);
  // A button for Mahanth's group (or "mahanth: …").
  assert.deepEqual(router.route("mahanth: ask group Admin & Networking", SUJITH, true, false, at(1)), { sender: MAHANTH, text: "ask group Admin & Networking" });
  // The reply that follows goes to Mahanth's assistant…
  assert.equal(typed("No, that's an old message, do not send", 2), MAHANTH);
  assert.equal(typed("also ask him about the domain renewal", 20), MAHANTH); // each reply keeps it open
  // …but commands and board buttons stay with Sujith's own.
  assert.equal(typed("add group Sales Team", 21), SUJITH);
  assert.equal(router.route("ask group Recruitment Team", SUJITH, true, false, at(21)).sender, SUJITH);
  // Half an hour of quiet: back to his own.
  assert.equal(typed("who has not replied?", 51), SUJITH);
  // "sujith: …" brings it straight back.
  router.route("mahanth: board", SUJITH, true, false, at(60));
  router.route("sujith: board", SUJITH, true, true, at(61));
  assert.equal(typed("thanks", 62), SUJITH);
  // The same the other way round, for Mahanth.
  router.route("sujith: ask group Recruitment Team", MAHANTH, true, false, at(70));
  assert.equal(typed("ok skip that", 71, MAHANTH), SUJITH);
  assert.equal(typed("list", 72, MAHANTH), MAHANTH);
  // Someone who is not an admin is never routed this way.
  assert.equal(router.route("no", "priya@mergencorp.com", false, true, at(73)).sender, SUJITH);
});

test("extra senders are read from the setting, each with a data folder of its own", () => {
  assert.deepEqual(extraSenders(" Mahanth.M@mergencorp.com , ", SUJITH), [MAHANTH]);
  assert.deepEqual(extraSenders(`${SUJITH},${MAHANTH}`, SUJITH), [MAHANTH]); // the main sender is not an extra
  assert.deepEqual(extraSenders("", SUJITH), []);
  assert.equal(senderDataDir("./data", MAHANTH), path.join("./data", "senders", MAHANTH));
});

test("each sender sees the groups the other senders follow, with the name to send them a request", () => {
  const store = (groups: string[]) => ({
    groups: () => groups.map((name, i) => ({ chatId: `c${i}`, name })),
    personByEmail: () => undefined,
  });
  const sujith = { name: "Sujith", email: SUJITH, store: store(["Recruitment Team"]) };
  const mahanth = { name: "Mahanth M", email: MAHANTH, store: store(["Admin & Networking", "Sales"]) };
  assert.deepEqual(othersGroupsFor(sujith, [sujith, mahanth])(), [{ name: "Mahanth M", key: "mahanth", groups: ["Admin & Networking", "Sales"] }]);
  assert.deepEqual(othersGroupsFor(mahanth, [sujith, mahanth])(), [{ name: "Sujith", key: "sujith", groups: ["Recruitment Team"] }]);
  assert.equal(senderKey(MAHANTH), "mahanth");
});

test("an extra sender without a timezone or office hours of their own takes the main sender's", () => {
  const kv = (init: Record<string, string>) => {
    const m = new Map(Object.entries(init));
    return { get: (k: string) => m.get(k), set: (k: string, v: string) => void m.set(k, v), m };
  };
  const main = kv({ timezone: "Asia/Kolkata", hours: "09:30-21:00@Asia/Kolkata" });
  const fresh = kv({});
  inheritSettings(main, fresh);
  assert.equal(fresh.get("timezone"), "Asia/Kolkata");
  assert.equal(fresh.get("hours"), "09:30-21:00@Asia/Kolkata");
  // Their own choice is kept.
  const own = kv({ timezone: "Europe/London" });
  inheritSettings(main, own);
  assert.equal(own.get("timezone"), "Europe/London");
  assert.equal(own.get("hours"), "09:30-21:00@Asia/Kolkata");
});

test("each sender is told which other sender already follows a group or person", () => {
  const store = (groups: string[], people: Record<string, boolean>) => ({
    groups: () => groups.map((chatId) => ({ chatId })),
    personByEmail: (email: string) => (email in people ? { nominated: people[email]! } : undefined),
  });
  const sujith = { name: "Sujith", store: store(["g-rec"], { "ravi@x.test": true, "old@x.test": false }) };
  const mahanth = { name: "Mahanth M", store: store(["g-sales"], { "anna@x.test": true }) };
  const forMahanth = followedElsewhereFor(mahanth, [sujith, mahanth]);
  assert.equal(forMahanth.group("g-rec"), "Sujith");
  assert.equal(forMahanth.group("g-sales"), undefined); // his own
  assert.equal(forMahanth.person("Ravi@X.test"), "Sujith");
  assert.equal(forMahanth.person("old@x.test"), undefined); // no longer followed
  assert.equal(followedElsewhereFor(sujith, [sujith, mahanth]).person("anna@x.test"), "Mahanth M");
});
