import test from "node:test";
import assert from "node:assert/strict";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  maximumNewsVideoSeconds,
  parseNewsPlaylist,
  readNewsTiming,
} from "../lib/news-timing.mjs";

test("parses serialized news playlist items", () => {
  const items = parseNewsPlaylist(`
  m_EditorClassIdentifier: Assembly-CSharp::DoodleDiplomacy.Gameplay.FirstContact.FirstContactNewsBroadcastPlayer
  playlist:
  - mediaType: 0
    videoClip: {fileID: 32900000, guid: 1234567890abcdef1234567890abcdef, type: 3}
    stillImage: {fileID: 0}
    stillImageSeconds: 3
  - mediaType: 1
    videoClip: {fileID: 0}
    stillImage: {fileID: 2800000, guid: abcdef1234567890abcdef1234567890, type: 3}
    stillImageSeconds: 5.5
  narrativeSettings: {fileID: 0}
`);
  assert.deepEqual(items, [
    {
      mediaType: 0,
      videoGuid: "1234567890abcdef1234567890abcdef",
      stillGuid: "",
      stillImageSeconds: 3,
    },
    {
      mediaType: 1,
      videoGuid: "",
      stillGuid: "abcdef1234567890abcdef1234567890",
      stillImageSeconds: 5.5,
    },
  ]);
});

test("reads the checked-in First Contact news durations", async () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const projectRoot = path.resolve(here, "../../../LlamaSharpDemo");
  const items = await readNewsTiming(projectRoot);

  assert.equal(items.length, 5);
  assert.deepEqual(items.map((item) => item.assetName), [
    "UFO_VHS.mp4",
    "reporter.mp4",
    "interview.mp4",
    "ufo_picture.jpg",
    "press_conference.mp4",
  ]);
  assert.ok(items.filter((item) => item.mediaType === "video").every((item) =>
    item.sourceSeconds > 9.9 && item.playbackSeconds <= maximumNewsVideoSeconds));
  assert.equal(items[3].playbackSeconds, 7);
});
