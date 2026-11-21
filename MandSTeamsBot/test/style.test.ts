import assert from "node:assert/strict";
import { test } from "node:test";
import { humanise, tidyReply } from "../src/ai.js";

test("thanks is removed, the rest of the message stays", () => {
  assert.equal(
    tidyReply("Ok, noted. Thanks for the update. Is there anything you need from me?", "Ravi Kumar"),
    "Ok, noted. Is there anything you need from me?",
  );
  assert.equal(tidyReply("Thank you!", null), "ok noted");
});

test("the person's name is removed wherever it appears", () => {
  assert.equal(tidyReply("Hi Ravi, any update on the 3 submissions?", "Ravi Kumar"), "Hi, any update on the 3 submissions?");
  assert.equal(tidyReply("Ok Ravi. What is the blocker?", "Ravi Kumar"), "Ok. What is the blocker?");
  assert.equal(tidyReply("What is the blocker, Ravi?", "Ravi Kumar"), "What is the blocker?");
});

test("line breaks are flattened to one line", () => {
  assert.equal(tidyReply("Ok.\n\nWhat is the blocker?", null), "Ok. What is the blocker?");
});

test("a name that is part of the person's own greeting is kept", () => {
  assert.equal(tidyReply("Hi Ravi, any update?", "Ravi Kumar", "Hi Ravi"), "Hi Ravi, any update?");
  assert.equal(tidyReply("Hi Anna, any update?", "Mahanth M", "Hi Anna"), "Hi Anna, any update?");
});

test("internal item numbers never reach the reader", () => {
  assert.equal(tidyReply("Hi. Item 7, the QA fixes, was due yesterday. Is it done?", null), "Hi. The QA fixes, was due yesterday. Is it done?");
  assert.equal(tidyReply("Ok. What about item 7, the QA fixes?", null), "Ok. What about the QA fixes?");
  assert.equal(tidyReply("Is item #12 done?", null), "Is done?");
});

test("lower case is left alone, and thanks is kept when the conversation has not had one yet", () => {
  assert.equal(tidyReply("ok. and the login page?", null), "ok. and the login page?");
  assert.equal(tidyReply("fine, thanks. see you tomorrow", null, "", true), "fine, thanks. see you tomorrow");
});

test("humanise sometimes drops the 'ok' in front and changes the first capital, never the whole message", () => {
  assert.equal(humanise("Ok, noted. Who is fixing it?", () => 0), "who is fixing it?");
  assert.equal(humanise("Ok, noted. Who is fixing it?", () => 0.99), "Ok, noted. Who is fixing it?");
  assert.equal(humanise("ok noted", () => 0), "Ok noted"); // a message that is only an acknowledgement stays
  assert.equal(humanise("I will check", () => 0), "I will check");
});
