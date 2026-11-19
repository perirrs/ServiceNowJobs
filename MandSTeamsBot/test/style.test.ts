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
