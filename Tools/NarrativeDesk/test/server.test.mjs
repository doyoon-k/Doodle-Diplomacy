import test from "node:test";
import assert from "node:assert/strict";
import path from "node:path";
import os from "node:os";
import fs from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { spawn } from "node:child_process";
import { WebSocket } from "ws";

const here = path.dirname(fileURLToPath(import.meta.url));
const deskRoot = path.resolve(here, "..");
const port = 4318;

test("serves the project index and authoring UI", async (context) => {
  const temporaryProject = await fs.mkdtemp(path.join(os.tmpdir(), "narrative-desk-"));
  const temporaryNarrativeFolder = path.join(temporaryProject, "Assets", "Narrative");
  await fs.mkdir(temporaryNarrativeFolder, { recursive: true });
  await fs.copyFile(
    path.resolve(deskRoot, "../../LlamaSharpDemo/Assets/Narrative/first_contact_day1.narrative.json"),
    path.join(temporaryNarrativeFolder, "first_contact_day1.narrative.json"),
  );
  context.after(() => fs.rm(temporaryProject, { recursive: true, force: true }));

  const server = spawn(process.execPath, ["server.mjs"], {
    cwd: deskRoot,
    env: { ...process.env, NARRATIVE_DESK_PORT: String(port), NARRATIVE_PROJECT_ROOT: temporaryProject },
    windowsHide: true,
    stdio: ["ignore", "pipe", "pipe"],
  });
  context.after(() => server.kill());

  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error("Narrative Desk did not start.")), 8000);
    server.once("exit", (code) => reject(new Error(`Narrative Desk exited with ${code}.`)));
    server.stdout.on("data", (chunk) => {
      if (chunk.toString().includes("Narrative Desk:")) {
        clearTimeout(timeout);
        resolve();
      }
    });
  });

  const projectResponse = await fetch(`http://127.0.0.1:${port}/api/project`);
  assert.equal(projectResponse.status, 200);
  const project = await projectResponse.json();
  assert.equal(project.scenarios[0].scenarioId, "first_contact_day1");
  assert.equal(project.scenarios[0].beatCount, 42);

  const scenarioResponse = await fetch(`http://127.0.0.1:${port}/api/scenarios/first_contact_day1`);
  const scenarioPayload = await scenarioResponse.json();
  scenarioPayload.document.title = "Temporary save test";
  const saveResponse = await fetch(`http://127.0.0.1:${port}/api/scenarios/first_contact_day1`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(scenarioPayload.document),
  });
  assert.equal(saveResponse.status, 200);
  assert.equal(
    JSON.parse(await fs.readFile(path.join(temporaryNarrativeFolder, "first_contact_day1.narrative.json"), "utf8")).title,
    "Temporary save test",
  );
  assert.equal(await fs.stat(path.join(temporaryNarrativeFolder, "first_contact_day1.narrative.json.bak")).then(() => true), true);

  const pageResponse = await fetch(`http://127.0.0.1:${port}/`);
  assert.equal(pageResponse.status, 200);
  const page = await pageResponse.text();
  assert.match(page, /Narrative Desk/);
  assert.match(page, /UI 문구/);

  const catalogResponse = await fetch(`http://127.0.0.1:${port}/api/localization/catalog`);
  assert.equal(catalogResponse.status, 200);
  const catalogPayload = await catalogResponse.json();
  catalogPayload.document.entries.push({
    key: "ui.test.button", sourceText: "TEST", domain: "ui.test", surface: "menu", screenId: "title.main",
    context: "test button", status: "draft", tags: [], localizedTexts: [{ locale: "ko-KR", text: "테스트", status: "draft" }],
  });
  const catalogSave = await fetch(`http://127.0.0.1:${port}/api/localization/catalog`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(catalogPayload.document),
  });
  assert.equal(catalogSave.status, 200);
  assert.equal(
    JSON.parse(await fs.readFile(path.join(temporaryProject, "Assets", "Localization", "Authoring", "ui_copy.catalog.json"), "utf8")).entries[0].key,
    "ui.test.button",
  );

  const unity = await openSocket("unity");
  const web = await openSocket("web");
  context.after(() => { unity.close(); web.close(); });
  const relayed = new Promise((resolve) => {
    web.on("message", (buffer) => {
      const message = JSON.parse(buffer.toString());
      if (message.type === "narrative_trace") resolve(message);
    });
  });
  unity.send(JSON.stringify({ type: "narrative_trace", trace: { beatId: "first_trace", phase: "enter" } }));
  assert.equal((await relayed).trace.beatId, "first_trace");

  const uiRelayed = new Promise((resolve) => {
    web.on("message", (buffer) => {
      const message = JSON.parse(buffer.toString());
      if (message.type === "ui_copy_trace") resolve(message);
    });
  });
  unity.send(JSON.stringify({ type: "ui_copy_trace", trace: { screenId: "title.main", keys: ["ui.title.start"] } }));
  assert.equal((await uiRelayed).trace.screenId, "title.main");

  function openSocket(role) {
    return new Promise((resolve, reject) => {
      const socket = new WebSocket(`ws://127.0.0.1:${port}/ws`);
      socket.once("error", reject);
      socket.once("open", () => {
        socket.send(JSON.stringify({ type: "hello", role }));
        setTimeout(() => resolve(socket), 20);
      });
    });
  }
});
