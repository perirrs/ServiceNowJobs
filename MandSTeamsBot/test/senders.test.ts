import assert from "node:assert/strict";
import { test } from "node:test";
import path from "node:path";
import { extraSenders, followedElsewhereFor, routeAdminMessage, senderDataDir } from "../src/senders.js";

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

test("extra senders are read from the setting, each with a data folder of its own", () => {
  assert.deepEqual(extraSenders(" Mahanth.M@mergencorp.com , ", SUJITH), [MAHANTH]);
  assert.deepEqual(extraSenders(`${SUJITH},${MAHANTH}`, SUJITH), [MAHANTH]); // the main sender is not an extra
  assert.deepEqual(extraSenders("", SUJITH), []);
  assert.equal(senderDataDir("./data", MAHANTH), path.join("./data", "senders", MAHANTH));
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
