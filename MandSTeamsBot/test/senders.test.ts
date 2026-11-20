import assert from "node:assert/strict";
import { test } from "node:test";
import path from "node:path";
import { extraSenders, routeAdminMessage, senderDataDir } from "../src/senders.js";

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
