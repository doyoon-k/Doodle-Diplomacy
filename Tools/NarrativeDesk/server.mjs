import http from "node:http";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { WebSocketServer, WebSocket } from "ws";
import { validateScenario } from "./lib/validation.mjs";
import { createCatalogFromUnityTable, validateCatalog } from "./lib/localization-catalog.mjs";
import { auditCatalogUsage } from "./lib/source-audit.mjs";

const deskRoot = path.dirname(fileURLToPath(import.meta.url));
const defaultProjectRoot = path.resolve(deskRoot, "../../LlamaSharpDemo");
const projectRoot = path.resolve(process.env.NARRATIVE_PROJECT_ROOT || process.argv[2] || defaultProjectRoot);
const narrativeRoot = path.join(projectRoot, "Assets", "Narrative");
const localizationAuthoringRoot = path.join(projectRoot, "Assets", "Localization", "Authoring");
const localizationCatalogPath = path.join(localizationAuthoringRoot, "ui_copy.catalog.json");
const unityStringTablePath = path.join(projectRoot, "Assets", "Resources", "Localization", "LocalizedStringTable.asset");
const publicRoot = path.join(deskRoot, "public");
const port = Number(process.env.NARRATIVE_DESK_PORT || 4317);
const host = "127.0.0.1";

const mimeTypes = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
};

const sendJson = (response, status, payload) => {
  response.writeHead(status, { "Content-Type": mimeTypes[".json"], "Cache-Control": "no-store" });
  response.end(JSON.stringify(payload));
};

const safeScenarioId = (value) => String(value || "").replace(/[^A-Za-z0-9_-]/g, "");
const scenarioPath = (scenarioId) => path.join(narrativeRoot, `${safeScenarioId(scenarioId)}.narrative.json`);

async function listScenarios() {
  await fs.mkdir(narrativeRoot, { recursive: true });
  const files = (await fs.readdir(narrativeRoot)).filter((name) => name.endsWith(".narrative.json"));
  const scenarios = [];
  for (const name of files.sort()) {
    try {
      const document = JSON.parse(await fs.readFile(path.join(narrativeRoot, name), "utf8"));
      const issues = validateScenario(document);
      scenarios.push({
        scenarioId: document.scenarioId,
        title: document.title,
        fileName: name,
        beatCount: document.beats?.length || 0,
        errorCount: issues.filter((issue) => issue.severity === "error").length,
        warningCount: issues.filter((issue) => issue.severity === "warning").length,
      });
    } catch (error) {
      scenarios.push({ scenarioId: name.replace(".narrative.json", ""), title: name, fileName: name, error: error.message });
    }
  }
  return scenarios;
}

async function narrativeBeatKeys() {
  const keys = new Set();
  await fs.mkdir(narrativeRoot, { recursive: true });
  const files = (await fs.readdir(narrativeRoot)).filter((name) => name.endsWith(".narrative.json"));
  for (const name of files) {
    try {
      const document = JSON.parse(await fs.readFile(path.join(narrativeRoot, name), "utf8"));
      for (const beat of document.beats || []) {
        const key = String(beat?.localizationKey || "").trim().toLowerCase();
        if (key) keys.add(key);
      }
    } catch {
      // Scenario parse errors are reported through the scenario endpoint.
    }
  }
  return keys;
}

async function loadCatalog() {
  try {
    return JSON.parse(await fs.readFile(localizationCatalogPath, "utf8"));
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
    const catalog = await createCatalogFromUnityTable(unityStringTablePath, await narrativeBeatKeys());
    await atomicWrite(localizationCatalogPath, `${JSON.stringify(catalog, null, 2)}\n`);
    return catalog;
  }
}

async function readBody(request) {
  const chunks = [];
  let length = 0;
  for await (const chunk of request) {
    length += chunk.length;
    if (length > 5_000_000) throw new Error("Request is larger than 5 MB.");
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString("utf8");
}

async function atomicWrite(filePath, text) {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  const temporary = `${filePath}.${process.pid}.tmp`;
  const backup = `${filePath}.bak`;
  try {
    await fs.copyFile(filePath, backup);
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
  }
  await fs.writeFile(temporary, text, "utf8");
  await fs.rename(temporary, filePath);
}

async function serveStatic(request, response) {
  const requestPath = new URL(request.url, `http://${request.headers.host}`).pathname;
  const relative = requestPath === "/" ? "index.html" : requestPath.slice(1);
  const filePath = path.resolve(publicRoot, relative);
  if (!filePath.startsWith(publicRoot)) return sendJson(response, 403, { error: "Forbidden" });
  try {
    const body = await fs.readFile(filePath);
    response.writeHead(200, {
      "Content-Type": mimeTypes[path.extname(filePath)] || "application/octet-stream",
      "Cache-Control": "no-store",
    });
    response.end(body);
  } catch (error) {
    sendJson(response, error.code === "ENOENT" ? 404 : 500, { error: error.message });
  }
}

const server = http.createServer(async (request, response) => {
  try {
    const url = new URL(request.url, `http://${request.headers.host}`);
    if (request.method === "GET" && url.pathname === "/api/project") {
      return sendJson(response, 200, { projectRoot, narrativeRoot, localizationCatalogPath, scenarios: await listScenarios() });
    }

    if (request.method === "GET" && url.pathname === "/api/localization/catalog") {
      const catalog = await loadCatalog();
      const beatKeys = await narrativeBeatKeys();
      return sendJson(response, 200, {
        document: catalog,
        issues: validateCatalog(catalog, beatKeys),
        usage: await auditCatalogUsage(projectRoot, catalog, beatKeys),
      });
    }

    if (request.method === "PUT" && url.pathname === "/api/localization/catalog") {
      const catalog = JSON.parse(await readBody(request));
      const issues = validateCatalog(catalog, await narrativeBeatKeys());
      const errors = issues.filter((issue) => issue.severity === "error");
      if (errors.length) return sendJson(response, 422, { error: "Validation failed.", issues });
      await atomicWrite(localizationCatalogPath, `${JSON.stringify(catalog, null, 2)}\n`);
      broadcast({ type: "catalog_saved", catalogId: catalog.catalogId, at: new Date().toISOString() });
      return sendJson(response, 200, {
        ok: true,
        issues,
        usage: await auditCatalogUsage(projectRoot, catalog, await narrativeBeatKeys()),
        savedAt: new Date().toISOString(),
      });
    }

    const scenarioMatch = url.pathname.match(/^\/api\/scenarios\/([A-Za-z0-9_-]+)$/);
    if (scenarioMatch && request.method === "GET") {
      const document = JSON.parse(await fs.readFile(scenarioPath(scenarioMatch[1]), "utf8"));
      return sendJson(response, 200, { document, issues: validateScenario(document) });
    }

    if (scenarioMatch && request.method === "PUT") {
      const document = JSON.parse(await readBody(request));
      if (safeScenarioId(document.scenarioId) !== scenarioMatch[1]) {
        return sendJson(response, 400, { error: "The URL and document scenarioId do not match." });
      }
      const issues = validateScenario(document);
      const errors = issues.filter((issue) => issue.severity === "error");
      if (errors.length) return sendJson(response, 422, { error: "Validation failed.", issues });

      await atomicWrite(scenarioPath(scenarioMatch[1]), `${JSON.stringify(document, null, 2)}\n`);
      broadcast({ type: "scenario_saved", scenarioId: document.scenarioId, at: new Date().toISOString() });
      return sendJson(response, 200, { ok: true, issues, savedAt: new Date().toISOString() });
    }

    if (url.pathname.startsWith("/api/")) return sendJson(response, 404, { error: "Not found" });
    return serveStatic(request, response);
  } catch (error) {
    sendJson(response, error.code === "ENOENT" ? 404 : 500, { error: error.message });
  }
});

const websocketServer = new WebSocketServer({ noServer: true });
const clients = new Set();

function broadcast(message, excluded = null, targetRole = "") {
  const text = JSON.stringify(message);
  for (const client of clients) {
    if (client === excluded || client.readyState !== WebSocket.OPEN) continue;
    if (targetRole && client.role !== targetRole) continue;
    client.send(text);
  }
}

websocketServer.on("connection", (socket) => {
  socket.role = "unknown";
  clients.add(socket);
  socket.send(JSON.stringify({ type: "server_hello", projectRoot, at: new Date().toISOString() }));
  socket.on("message", (buffer) => {
    try {
      const message = JSON.parse(buffer.toString("utf8"));
      if (message.type === "hello") {
        socket.role = message.role || "unknown";
        broadcastConnectionState();
        return;
      }
      if (socket.role === "unity") broadcast(message, socket, "web");
      else if (socket.role === "web") broadcast(message, socket, "unity");
    } catch (error) {
      socket.send(JSON.stringify({ type: "bridge_error", error: error.message }));
    }
  });
  socket.on("close", () => {
    clients.delete(socket);
    broadcastConnectionState();
  });
  broadcastConnectionState();
});

function broadcastConnectionState() {
  const roles = [...clients].reduce((counts, client) => {
    counts[client.role] = (counts[client.role] || 0) + 1;
    return counts;
  }, {});
  broadcast({ type: "connection_state", roles });
}

server.on("upgrade", (request, socket, head) => {
  if (new URL(request.url, `http://${request.headers.host}`).pathname !== "/ws") return socket.destroy();
  websocketServer.handleUpgrade(request, socket, head, (websocket) => websocketServer.emit("connection", websocket, request));
});

server.listen(port, host, () => {
  process.stdout.write(`Narrative Desk: http://${host}:${port}\nProject: ${projectRoot}\n`);
});
