import assert from "node:assert/strict";
import { test } from "node:test";
import { tidyReply } from "../src/ai.js";

test("thanks is removed, the rest of the message stays", () => {
  assert.equal(
    tidyReply("Ok, noted. Thanks for the update. Is there anything you need from me?", "Ravi Kumar"),
    "Ok, noted. Is there anything you need from me?",
  );
  assert.equal(tidyReply("Thank you!", null), "Ok, noted.");
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
