import { zipSync } from "fflate";
import type { WorkspaceFile } from "../types";

export interface WorkspaceAdapter {
  readonly name: string;
  readonly directWrite: boolean;
  listFiles(): Promise<string[]>;
  readText(path: string): Promise<string>;
  writeText(path: string, text: string): Promise<void>;
  delete(path: string): Promise<void>;
  rename(oldPath: string, newPath: string): Promise<void>;
  copyTree(oldPrefix: string, newPrefix: string): Promise<void>;
  deleteTree(prefix: string): Promise<void>;
  renameTree(oldPrefix: string, newPrefix: string): Promise<void>;
  exportZip(): Promise<Blob>;
}

const normalizePath = (path: string) => path.replaceAll("\\", "/").replace(/^\/+|\/+$/g, "");
const textEncoder = new TextEncoder();
const textDecoder = new TextDecoder();
const zipBlob = (entries: Record<string, Uint8Array>) => {
  // Copy into an ArrayBuffer-backed view. TypeScript 6 distinguishes it from SharedArrayBuffer.
  const archive = new Uint8Array(zipSync(entries));
  return new Blob([archive.buffer], { type: "application/zip" });
};

async function collectDirectory(
  directory: FileSystemDirectoryHandle,
  prefix = "",
  output: Map<string, FileSystemFileHandle> = new Map(),
): Promise<Map<string, FileSystemFileHandle>> {
  for await (const [name, handle] of directory.entries()) {
    const path = normalizePath(prefix ? `${prefix}/${name}` : name);
    if (handle.kind === "file") output.set(path, handle as FileSystemFileHandle);
    else await collectDirectory(handle as FileSystemDirectoryHandle, path, output);
  }
  return output;
}

async function resolveParent(
  root: FileSystemDirectoryHandle,
  path: string,
  create: boolean,
): Promise<{ parent: FileSystemDirectoryHandle; name: string }> {
  const parts = normalizePath(path).split("/").filter(Boolean);
  const name = parts.pop();
  if (!name) throw new Error("文件路径不能为空。");
  let parent = root;
  for (const segment of parts) parent = await parent.getDirectoryHandle(segment, { create });
  return { parent, name };
}

export class DirectoryWorkspaceAdapter implements WorkspaceAdapter {
  readonly directWrite = true;
  private backups = new Set<string>();
  private readonly sessionStamp = new Date().toISOString().replace(/[:.]/g, "-");

  constructor(private root: FileSystemDirectoryHandle) {}

  get name() { return this.root.name; }

  async listFiles() {
    return [...(await collectDirectory(this.root)).keys()].sort((a, b) => a.localeCompare(b));
  }

  async readText(path: string) {
    return textDecoder.decode(await this.readBytes(path));
  }

  private async readBytes(path: string) {
    const handles = await collectDirectory(this.root);
    const handle = handles.get(normalizePath(path));
    if (!handle) throw new Error(`找不到文件：${path}`);
    return new Uint8Array(await (await handle.getFile()).arrayBuffer());
  }

  private async backupOnce(path: string) {
    path = normalizePath(path);
    if (this.backups.has(path)) return;
    const exists = (await this.listFiles()).some((item) => normalizePath(item) === path);
    if (exists) {
      const original = await this.readBytes(path);
      const backupPath = `.shadowbus-editor-backups/${this.sessionStamp}/${path}`;
      await this.writeRaw(backupPath, original);
    }
    this.backups.add(path);
  }

  private async writeRaw(path: string, data: string | Uint8Array) {
    const { parent, name } = await resolveParent(this.root, path, true);
    const handle = await parent.getFileHandle(name, { create: true });
    const writable = await handle.createWritable();
    try {
      await writable.write(data);
      await writable.close();
    } catch (error) {
      await writable.abort(error);
      throw error;
    }
  }

  async writeText(path: string, text: string) {
    await this.backupOnce(path);
    await this.writeRaw(normalizePath(path), text);
  }

  async delete(path: string) {
    path = normalizePath(path);
    await this.backupOnce(path);
    const { parent, name } = await resolveParent(this.root, path, false);
    await parent.removeEntry(name, { recursive: true });
  }

  async rename(oldPath: string, newPath: string) {
    const data = await this.readBytes(oldPath);
    await this.backupOnce(newPath);
    await this.writeRaw(newPath, data);
    await this.delete(oldPath);
  }

  async deleteTree(prefix: string) {
    prefix = normalizePath(prefix);
    const paths = (await this.listFiles()).filter((path) => path === prefix || path.startsWith(`${prefix}/`));
    if (!paths.length) return;
    await Promise.all(paths.map((path) => this.backupOnce(path)));
    const { parent, name } = await resolveParent(this.root, prefix, false);
    await parent.removeEntry(name, { recursive: true });
  }

  async copyTree(oldPrefix: string, newPrefix: string) {
    oldPrefix = normalizePath(oldPrefix);
    newPrefix = normalizePath(newPrefix);
    const allPaths = await this.listFiles();
    const paths = allPaths.filter((path) => path.startsWith(`${oldPrefix}/`));
    if (!paths.length) throw new Error(`找不到目录：${oldPrefix}`);
    const existing = new Set(allPaths);
    const copies = paths.map((path) => ({ path, target: `${newPrefix}/${path.slice(oldPrefix.length + 1)}` }));
    if (copies.some(({ target }) => existing.has(target))) throw new Error(`目标目录已包含同名文件：${newPrefix}`);
    await Promise.all(copies.map(async ({ path, target }) => this.writeRaw(target, await this.readBytes(path))));
  }

  async renameTree(oldPrefix: string, newPrefix: string) {
    oldPrefix = normalizePath(oldPrefix);
    newPrefix = normalizePath(newPrefix);
    const paths = (await this.listFiles()).filter((path) => path.startsWith(`${oldPrefix}/`));
    if (!paths.length) throw new Error(`找不到目录：${oldPrefix}`);
    const existing = new Set(await this.listFiles());
    const moves = paths.map((path) => ({ path, target: `${newPrefix}/${path.slice(oldPrefix.length + 1)}` }));
    if (moves.some(({ target }) => existing.has(target))) throw new Error(`目标目录已包含同名文件：${newPrefix}`);
    await Promise.all(moves.map(async ({ path, target }) => {
      await this.backupOnce(target);
      await this.writeRaw(target, await this.readBytes(path));
    }));
    await this.deleteTree(oldPrefix);
  }

  async exportZip() {
    const paths = await this.listFiles();
    const entries: Record<string, Uint8Array> = {};
    await Promise.all(paths.map(async (path) => { entries[path] = await this.readBytes(path); }));
    return zipBlob(entries);
  }
}

export class ImportedWorkspaceAdapter implements WorkspaceAdapter {
  readonly directWrite = false;
  private files = new Map<string, WorkspaceFile>();

  constructor(name: string, files: Iterable<WorkspaceFile>) {
    this.name = name;
    for (const file of files) this.files.set(normalizePath(file.path), { ...file, data: file.data.slice(), path: normalizePath(file.path) });
  }

  readonly name: string;
  async listFiles() { return [...this.files.keys()].sort((a, b) => a.localeCompare(b)); }
  async readText(path: string) {
    const file = this.files.get(normalizePath(path));
    if (!file) throw new Error(`找不到文件：${path}`);
    return textDecoder.decode(file.data);
  }
  async writeText(path: string, text: string) {
    path = normalizePath(path);
    this.files.set(path, { path, data: textEncoder.encode(text), modified: true });
  }
  async delete(path: string) { this.files.delete(normalizePath(path)); }
  async rename(oldPath: string, newPath: string) {
    oldPath = normalizePath(oldPath); newPath = normalizePath(newPath);
    const file = this.files.get(oldPath);
    if (!file) throw new Error(`找不到文件：${oldPath}`);
    if (this.files.has(newPath)) throw new Error(`目标文件已存在：${newPath}`);
    this.files.set(newPath, { path: newPath, data: file.data.slice(), modified: true });
    this.files.delete(oldPath);
  }
  async deleteTree(prefix: string) {
    prefix = normalizePath(prefix);
    for (const path of [...this.files.keys()]) if (path === prefix || path.startsWith(`${prefix}/`)) this.files.delete(path);
  }
  async copyTree(oldPrefix: string, newPrefix: string) {
    oldPrefix = normalizePath(oldPrefix); newPrefix = normalizePath(newPrefix);
    const paths = [...this.files.keys()].filter((path) => path.startsWith(`${oldPrefix}/`));
    if (!paths.length) throw new Error(`找不到目录：${oldPrefix}`);
    const copies = paths.map((path) => ({ path, target: `${newPrefix}/${path.slice(oldPrefix.length + 1)}` }));
    if (copies.some(({ target }) => this.files.has(target))) throw new Error(`目标目录已包含同名文件：${newPrefix}`);
    for (const { path, target } of copies) {
      const file = this.files.get(path)!;
      this.files.set(target, { path: target, data: file.data.slice(), modified: true });
    }
  }
  async renameTree(oldPrefix: string, newPrefix: string) {
    oldPrefix = normalizePath(oldPrefix); newPrefix = normalizePath(newPrefix);
    const paths = [...this.files.keys()].filter((path) => path.startsWith(`${oldPrefix}/`));
    if (!paths.length) throw new Error(`找不到目录：${oldPrefix}`);
    const moves = paths.map((path) => ({ path, target: `${newPrefix}/${path.slice(oldPrefix.length + 1)}` }));
    if (moves.some(({ target }) => this.files.has(target))) throw new Error(`目标目录已包含同名文件：${newPrefix}`);
    for (const { path, target } of moves) {
      const file = this.files.get(path)!;
      this.files.set(target, { path: target, data: file.data.slice(), modified: true });
    }
    for (const { path } of moves) this.files.delete(path);
  }
  async exportZip() {
    const entries: Record<string, Uint8Array> = {};
    for (const [path, file] of this.files) entries[path] = file.data;
    return zipBlob(entries);
  }
}

export async function openDirectoryWorkspace(): Promise<WorkspaceAdapter> {
  if (!window.showDirectoryPicker) throw new Error("当前浏览器不支持直接目录访问，请使用 Edge 或 Chrome，或改用导入目录。");
  return new DirectoryWorkspaceAdapter(await window.showDirectoryPicker({ mode: "readwrite" }));
}

export async function importDirectory(files: FileList): Promise<WorkspaceAdapter> {
  const loaded: WorkspaceFile[] = [];
  await Promise.all([...files].map(async (file) => {
    const rawPath = file.webkitRelativePath || file.name;
    const parts = normalizePath(rawPath).split("/");
    if (parts.length > 1) parts.shift();
    loaded.push({ path: parts.join("/"), data: new Uint8Array(await file.arrayBuffer()), modified: false });
  }));
  return new ImportedWorkspaceAdapter("导入的 Mods", loaded);
}

export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
