import fs from "node:fs/promises";
import path from "node:path";

const sourceExtensions = new Set([".cs", ".asset", ".prefab", ".unity", ".json"]);
const skippedFolders = new Set(["Generated", "Library", "Logs", "Temp", "obj", ".git"]);

export async function auditCatalogUsage(projectRoot, catalog, narrativeKeys = new Set()) {
  const keys = new Map((catalog.entries || []).map((entry) => [String(entry.key || "").toLowerCase(), entry.key]));
  const byKey = Object.fromEntries((catalog.entries || []).map((entry) => [entry.key, { count: 0, references: [], dynamic: false }]));
  const missing = new Map();
  const assetsRoot = path.join(projectRoot, "Assets");
  const scanRoots = ["Scripts", "Data", "Narrative"]
    .map((folder) => path.join(assetsRoot, folder));
  const files = (await Promise.all(scanRoots.map((root) => collectSourceFiles(root)))).flat();
  for (const file of files) {
    const relative = path.relative(projectRoot, file).replaceAll("\\", "/");
    if (relative.endsWith("Assets/Localization/Authoring/ui_copy.catalog.json") ||
        relative.endsWith("Assets/Resources/Localization/LocalizedStringTable.asset") ||
        relative.endsWith("generated-keys.manifest.json") ||
        relative.endsWith("ui_copy.manifest.json") ||
        relative.endsWith(".bak")) continue;
    let text;
    try {
      const stat = await fs.stat(file);
      if (stat.size > 3_000_000) continue;
      text = await fs.readFile(file, "utf8");
    } catch {
      continue;
    }
    const lines = text.replace(/\r\n/g, "\n").split("\n");
    for (let index = 0; index < lines.length; index++) {
      const line = lines[index];
      const tokens = line.match(/[A-Za-z0-9_]+(?:\.[A-Za-z0-9_-]+)+/g) || [];
      for (const token of tokens) addReference(keys, byKey, token, relative, index + 1);

      if (relative.endsWith("FirstContactPresentation.cs")) {
        for (const match of line.matchAll(/\bT\(\s*"([^"]+)"/g)) {
          addReference(keys, byKey, `first_contact.terminal.${match[1]}`, relative, index + 1);
        }
        for (const match of line.matchAll(/\bHeader\(\s*"([^"]+)"/g)) {
          addReference(keys, byKey, `first_contact.terminal.header.${match[1]}`, relative, index + 1);
        }
        for (const match of line.matchAll(/\bSignalColor\(\s*"([^"]+)"/g)) {
          addReference(keys, byKey, `first_contact.terminal.color.${match[1]}`, relative, index + 1);
        }
      }

      for (const match of line.matchAll(/(?:L10n|GameL10n)\.T\(\s*"([^"]+)"/g)) {
        const key = match[1];
        const normalized = key.toLowerCase();
        if (!keys.has(normalized) && !narrativeKeys.has(normalized)) {
          const references = missing.get(key) || [];
          const reference = `${relative}:${index + 1}`;
          if (!references.includes(reference)) references.push(reference);
          missing.set(key, references);
        }
      }
    }
  }

  for (const entry of catalog.entries || []) {
    const key = String(entry.key || "");
    const dynamicReference = key.startsWith("first_contact.terminal.category.")
        ? "FirstContact category family (dynamic)"
        : key.startsWith("first_contact.terminal.meaning.")
          ? "FirstContact meaning family (dynamic)"
          : "";
    if (!dynamicReference) continue;
    const usage = byKey[entry.key];
    usage.dynamic = true;
    usage.count = Math.max(usage.count, 1);
    if (!usage.references.includes(dynamicReference)) usage.references.push(dynamicReference);
  }

  return {
    byKey,
    unusedKeys: Object.entries(byKey).filter(([, usage]) => usage.count === 0).map(([key]) => key),
    missingKeys: [...missing].map(([key, references]) => ({ key, references })),
  };
}

function addReference(keys, byKey, candidate, relative, lineNumber) {
  const canonical = keys.get(String(candidate || "").toLowerCase());
  if (!canonical) return;
  const usage = byKey[canonical];
  const reference = `${relative}:${lineNumber}`;
  if (!usage.references.includes(reference)) usage.references.push(reference);
  usage.count = usage.references.length;
}

async function collectSourceFiles(root) {
  const result = [];
  async function visit(folder) {
    let entries;
    try {
      entries = await fs.readdir(folder, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      if (entry.isDirectory() && skippedFolders.has(entry.name)) continue;
      const fullPath = path.join(folder, entry.name);
      if (entry.isDirectory()) await visit(fullPath);
      else if (sourceExtensions.has(path.extname(entry.name).toLowerCase())) result.push(fullPath);
    }
  }
  await visit(root);
  return result;
}
