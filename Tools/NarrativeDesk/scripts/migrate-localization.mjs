import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { createCatalogFromUnityTable } from "../lib/localization-catalog.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const deskRoot = path.resolve(here, "..");
const projectRoot = path.resolve(process.argv[2] || path.join(deskRoot, "../../LlamaSharpDemo"));
const narrativeRoot = path.join(projectRoot, "Assets", "Narrative");
const catalogPath = path.join(projectRoot, "Assets", "Localization", "Authoring", "ui_copy.catalog.json");
const tablePath = path.join(projectRoot, "Assets", "Resources", "Localization", "LocalizedStringTable.asset");
const force = process.argv.includes("--force");

try {
  await fs.access(catalogPath);
  if (!force) {
    process.stdout.write(`UI copy catalog already exists at ${catalogPath}. Migration skipped. Pass --force only for a deliberate re-import.\n`);
    process.exitCode = 0;
    process.exit();
  }
} catch {
  // First migration: continue.
}

const narrativeFiles = (await fs.readdir(narrativeRoot))
  .filter((name) => name.endsWith(".narrative.json"))
  .map((name) => path.join(narrativeRoot, name));
const documents = [];
const beatKeys = new Set();
for (const file of narrativeFiles) {
  const document = JSON.parse(await fs.readFile(file, "utf8"));
  documents.push({ file, document });
  for (const beat of document.beats || []) {
    const key = String(beat?.localizationKey || "").trim().toLowerCase();
    if (key) beatKeys.add(key);
  }
}

const catalog = await createCatalogFromUnityTable(tablePath, beatKeys);
const catalogByKey = new Map(catalog.entries.map((entry) => [entry.key.toLowerCase(), entry]));
for (const { file, document } of documents) {
  const migrated = [];
  for (const extra of document.localizationEntries || []) {
    const entry = catalogByKey.get(String(extra?.key || "").toLowerCase());
    if (!entry) continue;
    entry.context = [extra.group, extra.beatId].filter(Boolean).join(" · ") || entry.context;
    entry.tags = [...new Set([...(entry.tags || []), "migrated-narrative-extra"])];
    for (const translated of extra.localizedTexts || []) {
      const catalogTranslation = entry.localizedTexts.find((item) => item.locale === translated.locale);
      if (catalogTranslation) catalogTranslation.status = translated.status || catalogTranslation.status;
    }
    migrated.push(extra.key);
  }
  if (!migrated.length) continue;
  document.localizationEntries = (document.localizationEntries || []).filter((extra) => !migrated.includes(extra.key));
  await fs.copyFile(file, `${file}.bak`);
  await fs.writeFile(file, `${JSON.stringify(document, null, 2)}\n`, "utf8");
}

await fs.mkdir(path.dirname(catalogPath), { recursive: true });
try {
  await fs.copyFile(catalogPath, `${catalogPath}.bak`);
} catch (error) {
  if (error.code !== "ENOENT") throw error;
}
await fs.writeFile(catalogPath, `${JSON.stringify(catalog, null, 2)}\n`, "utf8");
process.stdout.write(`Migrated ${catalog.entries.length} UI strings to ${catalogPath}\n`);
