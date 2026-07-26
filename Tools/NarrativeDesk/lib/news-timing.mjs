import fs from "node:fs/promises";
import path from "node:path";

export const maximumNewsVideoSeconds = 12;

export async function readNewsTiming(projectRoot) {
  const scenePath = path.join(
    projectRoot,
    "Assets",
    "Scenes",
    "FirstContact",
    "FC_Intro_Surface.unity",
  );
  let scene;
  try {
    scene = await fs.readFile(scenePath, "utf8");
  } catch (error) {
    if (error.code === "ENOENT") return [];
    throw error;
  }

  const serializedItems = parseNewsPlaylist(scene);
  if (!serializedItems.length) return [];
  const guidPaths = await readGuidPaths(path.join(projectRoot, "Assets", "Video", "FirstContact", "News"));
  const result = [];

  for (let index = 0; index < serializedItems.length; index++) {
    const item = serializedItems[index];
    const guid = item.mediaType === 0 ? item.videoGuid : item.stillGuid;
    const assetPath = guidPaths.get(guid) || "";
    const relativePath = assetPath
      ? path.relative(projectRoot, assetPath).replaceAll("\\", "/")
      : "";
    const assetName = assetPath ? path.basename(assetPath) : `News item ${index + 1}`;

    if (item.mediaType === 1) {
      const playbackSeconds = Math.max(0.1, Number(item.stillImageSeconds || 0));
      result.push({
        index,
        triggerEvent: `intro.news.clip.${index}`,
        mediaType: "still",
        assetName,
        assetPath: relativePath,
        sourceSeconds: playbackSeconds,
        playbackSeconds,
        capped: false,
      });
      continue;
    }

    let sourceSeconds = null;
    if (assetPath) {
      try {
        sourceSeconds = await readMp4Duration(assetPath);
      } catch {
        sourceSeconds = null;
      }
    }
    const playbackSeconds = sourceSeconds && sourceSeconds > 0
      ? Math.min(sourceSeconds, maximumNewsVideoSeconds)
      : maximumNewsVideoSeconds;
    result.push({
      index,
      triggerEvent: `intro.news.clip.${index}`,
      mediaType: "video",
      assetName,
      assetPath: relativePath,
      sourceSeconds,
      playbackSeconds,
      capped: Boolean(sourceSeconds && sourceSeconds > maximumNewsVideoSeconds),
    });
  }

  return result;
}

export function parseNewsPlaylist(sceneText) {
  const lines = String(sceneText || "").replace(/\r\n/g, "\n").split("\n");
  const componentIndex = lines.findIndex((line) =>
    line.includes("FirstContactNewsBroadcastPlayer"));
  if (componentIndex < 0) return [];
  const playlistIndex = lines.findIndex((line, index) =>
    index > componentIndex && /^\s{2}playlist:\s*$/.test(line));
  if (playlistIndex < 0) return [];

  const items = [];
  let current = null;
  for (let index = playlistIndex + 1; index < lines.length; index++) {
    const line = lines[index];
    if (/^\s{2}[A-Za-z_][A-Za-z0-9_]*:/.test(line) && !/^\s{2}- /.test(line)) break;
    const mediaMatch = line.match(/^\s{2}- mediaType:\s*(\d+)/);
    if (mediaMatch) {
      current = { mediaType: Number(mediaMatch[1]), videoGuid: "", stillGuid: "", stillImageSeconds: 0 };
      items.push(current);
      continue;
    }
    if (!current) continue;
    const videoMatch = line.match(/^\s{4}videoClip:.*guid:\s*([a-f0-9]{32})/i);
    const stillMatch = line.match(/^\s{4}stillImage:.*guid:\s*([a-f0-9]{32})/i);
    const secondsMatch = line.match(/^\s{4}stillImageSeconds:\s*([0-9.]+)/);
    if (videoMatch) current.videoGuid = videoMatch[1];
    if (stillMatch) current.stillGuid = stillMatch[1];
    if (secondsMatch) current.stillImageSeconds = Number(secondsMatch[1]);
  }
  return items;
}

export async function readMp4Duration(filePath) {
  const buffer = await fs.readFile(filePath);
  const moov = findAtom(buffer, 0, buffer.length, "moov");
  if (!moov) return null;
  const mvhd = findAtom(buffer, moov.contentStart, moov.end, "mvhd");
  if (!mvhd) return null;

  const version = buffer.readUInt8(mvhd.contentStart);
  let timescale;
  let duration;
  if (version === 1) {
    timescale = buffer.readUInt32BE(mvhd.contentStart + 20);
    duration = Number(buffer.readBigUInt64BE(mvhd.contentStart + 24));
  } else {
    timescale = buffer.readUInt32BE(mvhd.contentStart + 12);
    duration = buffer.readUInt32BE(mvhd.contentStart + 16);
  }
  if (!timescale || !Number.isFinite(duration)) return null;
  return duration / timescale;
}

function findAtom(buffer, start, end, targetType) {
  let offset = start;
  while (offset + 8 <= end) {
    let size = buffer.readUInt32BE(offset);
    const type = buffer.toString("ascii", offset + 4, offset + 8);
    let headerSize = 8;
    if (size === 1) {
      if (offset + 16 > end) return null;
      size = Number(buffer.readBigUInt64BE(offset + 8));
      headerSize = 16;
    } else if (size === 0) {
      size = end - offset;
    }
    if (!Number.isFinite(size) || size < headerSize || offset + size > end) return null;
    if (type === targetType) {
      return { contentStart: offset + headerSize, end: offset + size };
    }
    offset += size;
  }
  return null;
}

async function readGuidPaths(root) {
  const result = new Map();
  let entries;
  try {
    entries = await fs.readdir(root, { withFileTypes: true });
  } catch (error) {
    if (error.code === "ENOENT") return result;
    throw error;
  }
  for (const entry of entries) {
    const fullPath = path.join(root, entry.name);
    if (entry.isDirectory()) {
      const nested = await readGuidPaths(fullPath);
      for (const [guid, assetPath] of nested) result.set(guid, assetPath);
      continue;
    }
    if (!entry.name.endsWith(".meta")) continue;
    const meta = await fs.readFile(fullPath, "utf8");
    const match = meta.match(/^guid:\s*([a-f0-9]{32})/m);
    if (match) result.set(match[1], fullPath.slice(0, -5));
  }
  return result;
}
