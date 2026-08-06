import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { BossRushPackage, CardMasterPatch, CsvDocument, CustomFormat, EditorDocument, IdCatalog, TwoPickRule, ValidationIssue } from "./types";
import { emptyCatalog, mergeCatalogEntries, parseCharacterCatalog, parseQuestAiCatalog } from "./data/catalog";
import { importDirectory, openDirectoryWorkspace, downloadBlob, type WorkspaceAdapter } from "./workspace/workspace";
import { scanWorkspace, type ModuleFiles, type ModuleId } from "./workspace/scanner";
import { normalizeBossRush, normalizeCardMaster, normalizeFormat, normalizeTwoPick } from "./models/normalize";
import { newBossRush, newCardPatch, newFormat, newTwoPick } from "./models/defaults";
import { normalizeDeckCsv, parseCsv, serializeCsv } from "./models/csv";
import { validateBossRush, validateCardMaster, validateCsv, validateFormat, validateTwoPick } from "./models/validation";
import { ValidationPanel } from "./components/ValidationPanel";
import { BossRushEditor } from "./editors/BossRushEditor";
import { CardMasterEditor } from "./editors/CardMasterEditor";
import { FormatEditor } from "./editors/FormatEditor";
import { TwoPickEditor } from "./editors/TwoPickEditor";
import { AiDataEditor, ReferenceViewer } from "./editors/AiDataEditor";
import { App as AntdApp, Avatar, Badge, Breadcrumb, Button, ConfigProvider, Empty, Flex, Form, Input, Layout, Menu, Modal, Space, Spin, Tag, theme, Tooltip, Typography, message } from "antd";
import { CopyOutlined, DeleteOutlined, DownloadOutlined, EditOutlined, FileAddOutlined, FolderOpenOutlined, ImportOutlined, MenuFoldOutlined, MenuUnfoldOutlined, ReloadOutlined, SaveOutlined, SettingOutlined } from "@ant-design/icons";

type DocumentValue = BossRushPackage | CardMasterPatch[] | CustomFormat | TwoPickRule | CsvDocument | string;
type LoadedDocument = EditorDocument<DocumentValue> & { module: ModuleId; csvType?: "deck" | "style" | "emote" };

const labels: Record<ModuleId, string> = { bossrush: "BossRush", aidata: "AIData", cardmaster: "CardMaster", format: "Format", twopick: "TwoPick", reference: "AI 参考" };
const moduleOrder: ModuleId[] = ["bossrush", "aidata", "cardmaster", "format", "twopick", "reference"];
const emptyModuleFiles = (): ModuleFiles => ({ bossrush: [], aidata: [], cardmaster: [], format: [], twopick: [], reference: [] });
const { Header, Sider, Content } = Layout;

function moduleFromPath(path: string): { module: ModuleId; csvType?: "deck" | "style" | "emote" } {
  const lower = path.toLowerCase();
  if (lower.startsWith("bossrush/reference/") || /^aidata\/ai_.*\.json$/.test(lower)) return { module: "reference" };
  if (/^bossrush\/[^/]+\/ai\/(deck|style|emote)\//.test(lower)) return { module: "aidata", csvType: lower.includes("/style/") ? "style" : lower.includes("/emote/") ? "emote" : "deck" };
  if (lower.startsWith("bossrush/")) return { module: "bossrush" };
  if (lower.startsWith("aidata/")) return { module: "aidata", csvType: lower.includes("/style/") ? "style" : lower.includes("/emote/") ? "emote" : "deck" };
  if (lower.startsWith("cardmaster/")) return { module: "cardmaster" };
  if (lower.startsWith("format/")) return { module: "format" };
  return { module: "twopick" };
}

function parseDocument(path: string, text: string): LoadedDocument {
  const kind = moduleFromPath(path);
  let value: DocumentValue;
  if (kind.module === "reference") value = text;
  else if (kind.module === "aidata") value = kind.csvType === "deck" ? normalizeDeckCsv(parseCsv(text)) : parseCsv(text);
  else {
    const json = JSON.parse(text) as unknown;
    value = kind.module === "bossrush" ? normalizeBossRush(json) : kind.module === "cardmaster" ? normalizeCardMaster(json) : kind.module === "format" ? normalizeFormat(json) : normalizeTwoPick(json);
  }
  return { path, value, sourceText: text, dirty: false, ...kind };
}

function serializeDocument(document: LoadedDocument) {
  if (document.module === "reference") return String(document.value);
  if (document.module === "aidata") return serializeCsv(document.value as CsvDocument) + "\n";
  return JSON.stringify(document.value, null, 2) + "\n";
}

function validateDocument(document: LoadedDocument): ValidationIssue[] {
  if (document.module === "bossrush") return validateBossRush(document.value as BossRushPackage);
  if (document.module === "cardmaster") return validateCardMaster(document.value as CardMasterPatch[]);
  if (document.module === "format") return validateFormat(document.value as CustomFormat);
  if (document.module === "twopick") return validateTwoPick(document.value as TwoPickRule);
  if (document.module === "aidata") return validateCsv(document.value as CsvDocument, document.csvType!);
  return [];
}

function suggestedPath(module: ModuleId, id: string, aiType: "deck" | "style" | "emote" = "deck") {
  const safe = id.trim().replace(/[^a-zA-Z0-9_-]+/g, "_") || "new_config";
  if (module === "bossrush") return `BossRush/${safe}/bossrush.json`;
  if (module === "cardmaster") return `CardMaster/${safe}.json`;
  if (module === "format") return `Format/${safe}.json`;
  if (module === "twopick") return `TwoPick/${safe}.json`;
  return `AIData/${aiType}/${safe}.csv`;
}

export function App() {
  const [workspace, setWorkspace] = useState<WorkspaceAdapter | null>(null);
  const [files, setFiles] = useState<ModuleFiles>(emptyModuleFiles());
  const [activeModule, setActiveModule] = useState<ModuleId>("bossrush");
  const [document, setDocument] = useState<LoadedDocument | null>(null);
  const [catalog, setCatalog] = useState<IdCatalog>(emptyCatalog());
  const [status, setStatus] = useState("请选择 Shadowverse 的 Mods 目录，或导入一个 Mods 目录副本。");
  const [busy, setBusy] = useState(false);
  const [collapsed, setCollapsed] = useState(false);
  const [textDialog, setTextDialog] = useState<{ title: string; label: string; placeholder?: string } | null>(null);
  const [textDialogValue, setTextDialogValue] = useState("");
  const textDialogResolver = useRef<((value: string | null) => void) | null>(null);
  const [confirmDialog, setConfirmDialog] = useState<{ title: string; content: string; okText: string; danger?: boolean } | null>(null);
  const confirmDialogResolver = useRef<((value: boolean) => void) | null>(null);
  const importRef = useRef<HTMLInputElement>(null);
  const issues = useMemo(() => document ? validateDocument(document) : [], [document]);
  const hasErrors = issues.some((issue) => issue.severity === "error");
  const hasWarnings = issues.some((issue) => issue.severity === "warning");

  const requestText = (title: string, label: string, initialValue = "", placeholder?: string) => new Promise<string | null>((resolve) => {
    textDialogResolver.current = resolve;
    setTextDialogValue(initialValue);
    setTextDialog({ title, label, placeholder });
  });

  const finishTextDialog = (value: string | null) => {
    textDialogResolver.current?.(value);
    textDialogResolver.current = null;
    setTextDialog(null);
  };

  const requestConfirm = (title: string, content: string, okText = "继续", danger = false) => new Promise<boolean>((resolve) => {
    confirmDialogResolver.current = resolve;
    setConfirmDialog({ title, content, okText, danger });
  });

  const finishConfirmDialog = (value: boolean) => {
    confirmDialogResolver.current?.(value);
    confirmDialogResolver.current = null;
    setConfirmDialog(null);
  };

  useEffect(() => {
    const handler = (event: BeforeUnloadEvent) => { if (document?.dirty) { event.preventDefault(); event.returnValue = ""; } };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [document?.dirty]);

  const refresh = useCallback(async (nextWorkspace = workspace) => {
    if (!nextWorkspace) return;
    const scanned = await scanWorkspace(nextWorkspace);
    setFiles(scanned);
    const nextCatalog = emptyCatalog();
    const charPath = scanned.reference.find((path) => path.toLowerCase().endsWith("enemy_chara_ids.csv"));
    const manifestPath = scanned.reference.find((path) => path.toLowerCase().endsWith("manifest.json"));
    if (charPath) {
      const parsed = parseCharacterCatalog(await nextWorkspace.readText(charPath));
      nextCatalog.characters = mergeCatalogEntries(nextCatalog.characters, parsed, (entry) => entry.id);
    }
    if (manifestPath) {
      const parsed = parseQuestAiCatalog(await nextWorkspace.readText(manifestPath));
      nextCatalog.questAi = mergeCatalogEntries(nextCatalog.questAi, parsed, (entry) => entry.enemyAiId);
    }
    setCatalog(nextCatalog);
    setStatus(`已扫描 ${Object.values(scanned).flat().length} 个可编辑或参考文件。`);
  }, [workspace]);

  const attachWorkspace = async (next: WorkspaceAdapter) => {
    if (document?.dirty && !(await requestConfirm("切换工作区", "当前文件尚未保存，仍要切换目录吗？", "切换"))) return;
    setBusy(true);
    try { setWorkspace(next); setDocument(null); await refresh(next); const text = `已打开 ${next.name}${next.directWrite ? "（直接读写）" : "（导入副本）"}。`; setStatus(text); message.success(text); }
    catch (error) { const text = `打开失败：${error instanceof Error ? error.message : String(error)}`; setStatus(text); message.error(text); }
    finally { setBusy(false); }
  };

  const openFile = async (path: string) => {
    if (!workspace) return;
    if (document?.dirty && !(await requestConfirm("打开其他文件", "当前文件尚未保存，仍要打开其他文件吗？", "打开"))) return;
    setBusy(true);
    try { const next = parseDocument(path, await workspace.readText(path)); setDocument(next); setActiveModule(next.module); setStatus(`已打开 ${path}`); }
    catch (error) { const text = `解析失败：${error instanceof Error ? error.message : String(error)}`; setStatus(text); message.error(text); }
    finally { setBusy(false); }
  };

  const changeValue = (value: DocumentValue) => setDocument((current) => current ? { ...current, value, dirty: true } : current);

  const save = async () => {
    if (!workspace || !document || document.module === "reference") return;
    if (hasErrors) { setStatus("存在阻止保存的错误，请先修正。"); return; }
    if (hasWarnings && !(await requestConfirm("配置存在警告", "配置仍有警告，确定保存吗？", "仍然保存"))) return;
    setBusy(true);
    try { const text = serializeDocument(document); await workspace.writeText(document.path, text); setDocument({ ...document, sourceText: text, dirty: false }); await refresh(); const savedText = `已保存 ${document.path}${workspace.directWrite ? "；本会话首次覆盖前已创建备份。" : "（将在导出 ZIP 时写回）。"}`; setStatus(savedText); message.success(savedText); }
    catch (error) { const text = `保存失败：${error instanceof Error ? error.message : String(error)}`; setStatus(text); message.error(text); }
    finally { setBusy(false); }
  };

  const createFile = async () => {
    if (!workspace || activeModule === "reference") return;
    const id = await requestText("新建配置文件", "配置或文件 ID", activeModule === "bossrush" ? "new_bossrush" : activeModule === "cardmaster" ? "new_cards" : activeModule === "format" ? "new_format" : activeModule === "twopick" ? "new_twopick" : "new_ai", "例如 default 或 my_bossrush");
    if (!id) return;
    let path: string, value: DocumentValue, csvType: "deck" | "style" | "emote" | undefined;
    if (activeModule === "aidata") {
      const type = (await requestText("新建 AI CSV", "CSV 类型", "deck", "deck / style / emote") || "deck").toLowerCase();
      csvType = type === "style" || type === "emote" ? type : "deck";
      path = suggestedPath(activeModule, id, csvType);
      const headers = csvType === "deck" ? ["CardID", "UseCommon", "CardName", "CardNum", "BattleBonus", "PlayBonus", "Priority", "End"] : csvType === "style" ? ["ID", "Category", "Priority", "Type", "Arg", "Cond"] : ["ID", "Category", "FaceID", "MotionID", "VoiceID", "TextID"];
      value = { headers, rows: [], newline: "\r\n" } as CsvDocument;
    } else if (activeModule === "bossrush") { path = suggestedPath(activeModule, id); value = newBossRush(id); }
    else if (activeModule === "cardmaster") { path = suggestedPath(activeModule, id); value = [newCardPatch()]; }
    else if (activeModule === "format") { path = suggestedPath(activeModule, id); value = newFormat(id); }
    else { path = suggestedPath(activeModule, id); value = newTwoPick(id); }
    if ((await workspace.listFiles()).some((item) => item.toLowerCase() === path.toLowerCase())) { setStatus(`文件已存在：${path}`); return; }
    const next: LoadedDocument = { path, value, sourceText: "", dirty: true, module: activeModule, csvType };
    setDocument(next); setStatus(`已创建 ${path}，尚未保存。`);
  };

  const duplicateFile = async () => {
    if (!workspace || !document || document.module === "reference") return;
    if (document.dirty && !(await requestConfirm("复制未保存配置", "当前文件有未保存修改。副本将以编辑器中的当前内容创建，是否继续？", "创建副本"))) return;
    const id = await requestText("复制配置文件", "副本 ID", "copy", "输入新的文件或配置 ID"); if (!id) return;
    const path = suggestedPath(document.module, id, document.csvType);
    if ((await workspace.listFiles()).some((item) => item.toLowerCase() === path.toLowerCase())) { setStatus(`目标已存在：${path}`); return; }
    let value = structuredClone(document.value);
    if (document.module === "bossrush") value = { ...(value as BossRushPackage), id };
    if (document.module === "format") value = { ...(value as CustomFormat), id };
    if (document.module === "twopick") value = { ...(value as TwoPickRule), id };
    if (document.module === "bossrush") {
      try {
        const oldDirectory = document.path.split("/").slice(0, -1).join("/");
        const newDirectory = path.split("/").slice(0, -1).join("/");
        await workspace.copyTree(oldDirectory, newDirectory);
        const next = { ...document, path, value };
        const text = serializeDocument(next);
        await workspace.writeText(path, text);
        setDocument({ ...next, sourceText: text, dirty: false });
        await refresh();
        setStatus(`已复制完整 BossRush 配置包到 ${newDirectory}`);
      } catch (error) { const text = `复制失败：${error instanceof Error ? error.message : String(error)}`; setStatus(text); message.error(text); }
      return;
    }
    setDocument({ ...document, path, value, sourceText: "", dirty: true }); setStatus(`已创建副本 ${path}，尚未保存。`);
  };

  const renameFile = async () => {
    if (!workspace || !document || document.dirty || document.module === "reference") { setStatus("请先保存当前修改，再重命名文件。"); return; }
    const suggested = document.path.split("/").at(-1)?.replace(/\.(json|csv)$/i, "") ?? "config";
    const id = await requestText("重命名配置文件", "新的文件 ID", suggested); if (!id) return;
    const newPath = suggestedPath(document.module, id, document.csvType);
    const currentPaths = await workspace.listFiles();
    if (currentPaths.some((path) => path.toLowerCase() === newPath.toLowerCase())) { setStatus(`目标已存在：${newPath}`); return; }
    try {
      let value = structuredClone(document.value);
      const syncId = ["bossrush", "format", "twopick"].includes(document.module) && await requestConfirm("同步内部 ID", "同时将配置内部的 id 字段改为新的 ID 吗？", "同步");
      if (syncId) value = { ...(value as BossRushPackage | CustomFormat | TwoPickRule), id };
      if (document.module === "bossrush") {
        const oldDirectory = document.path.split("/").slice(0, -1).join("/");
        const newDirectory = newPath.split("/").slice(0, -1).join("/");
        await workspace.renameTree(oldDirectory, newDirectory);
      } else await workspace.rename(document.path, newPath);
      if (syncId) await workspace.writeText(newPath, JSON.stringify(value, null, 2) + "\n");
      setDocument({ ...document, path: newPath, value, sourceText: serializeDocument({ ...document, path: newPath, value }), dirty: false });
      await refresh(); setStatus(`已重命名为 ${newPath}${syncId ? "，并同步内部 ID" : ""}`);
    } catch (error) { const text = `重命名失败：${error instanceof Error ? error.message : String(error)}`; setStatus(text); message.error(text); }
  };

  const deleteFile = async () => {
    if (!workspace || !document || document.module === "reference" || !(await requestConfirm("删除配置文件", `确定删除 ${document.path} 吗？直接读写模式会先备份。`, "删除", true))) return;
    try {
      if (document.module === "bossrush") await workspace.deleteTree(document.path.split("/").slice(0, -1).join("/"));
      else await workspace.delete(document.path);
      setDocument(null); await refresh(); setStatus(document.module === "bossrush" ? "BossRush 配置包已删除。" : "文件已删除。");
    } catch (error) { const text = `删除失败：${error instanceof Error ? error.message : String(error)}`; setStatus(text); message.error(text); }
  };

  const renderEditor = () => {
    if (!document) return <div className="empty-state"><div className="empty-icon">◇</div><h2>选择或新建一个配置文件</h2><p>左侧按模块显示当前 Mods 目录中的配置。所有编辑都只在本地完成。</p></div>;
    if (document.module === "bossrush") return <BossRushEditor value={document.value as BossRushPackage} onChange={changeValue} catalog={catalog} />;
    if (document.module === "cardmaster") return <CardMasterEditor value={document.value as CardMasterPatch[]} onChange={changeValue} />;
    if (document.module === "format") return <FormatEditor value={document.value as CustomFormat} onChange={changeValue} />;
    if (document.module === "twopick") return <TwoPickEditor value={document.value as TwoPickRule} onChange={changeValue} />;
    if (document.module === "aidata") return <AiDataEditor type={document.csvType!} value={document.value as CsvDocument} onChange={changeValue} />;
    return <ReferenceViewer path={document.path} text={document.value as string} />;
  };

  const fileItems = files[activeModule];
  const pathParts = document?.path.split("/").filter(Boolean) ?? [labels[activeModule]];
  return <ConfigProvider theme={{ algorithm: theme.defaultAlgorithm, token: { colorPrimary: "#1677ff", borderRadius: 8, colorBgLayout: "#f5f7fb", fontFamily: "Inter, Segoe UI, Microsoft YaHei, sans-serif", motionDurationMid: "0.2s", boxShadowSecondary: "0 8px 24px rgba(16, 24, 40, 0.08)" } }}>
    <AntdApp>
      <Layout className="app-shell">
        <Header className="topbar">
          <Space className="brand-area" size="middle"><Avatar className="brand-mark" shape="square">S</Avatar><span><Typography.Title level={4}>Shadowbus 配置工作台</Typography.Title><Typography.Text type="secondary">本地优先 · GitHub Pages</Typography.Text></span></Space>
          <Space className="top-actions" size="small">
            <Tooltip title={collapsed ? "展开侧栏" : "收起侧栏"}><Button aria-label={collapsed ? "展开侧栏" : "收起侧栏"} type="text" icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />} onClick={() => setCollapsed((value) => !value)} /></Tooltip>
            <Button aria-label="打开 Mods 目录" type="primary" icon={<FolderOpenOutlined />} loading={busy} onClick={async () => { try { await attachWorkspace(await openDirectoryWorkspace()); } catch (error) { setStatus(error instanceof Error ? error.message : String(error)); } }}>打开 Mods 目录</Button>
            <Button aria-label="导入目录" icon={<ImportOutlined />} disabled={busy} onClick={() => importRef.current?.click()}>导入目录</Button>
            <input ref={importRef} className="hidden" type="file" multiple webkitdirectory="" onChange={async (event) => { if (event.target.files?.length) await attachWorkspace(await importDirectory(event.target.files)); event.target.value = ""; }} />
            {workspace && <Button aria-label="导出 ZIP" icon={<DownloadOutlined />} onClick={async () => downloadBlob(await workspace.exportZip(), `Shadowbus-Mods-${new Date().toISOString().slice(0, 10)}.zip`)}>导出 ZIP</Button>}
          </Space>
        </Header>
        <Layout className="body-layout">
          <Sider className="sidebar" width={292} collapsedWidth={76} collapsible collapsed={collapsed} trigger={null} theme="light">
            <div className="workspace-bar"><Badge status={workspace ? "success" : "default"} /><Typography.Text type="secondary" ellipsis>{collapsed ? (workspace ? "已连接" : "未连接") : (workspace ? `${workspace.name} · ${workspace.directWrite ? "直接读写" : "导入副本"}` : "尚未打开工作区")}</Typography.Text></div>
            <nav className="module-tabs"><Menu mode="inline" selectedKeys={[activeModule]} items={moduleOrder.map((module) => ({ key: module, icon: <SettingOutlined />, label: <span className="module-menu-label"><span>{labels[module]}</span><Badge count={files[module].length} showZero size="small" /></span> }))} onClick={({ key }) => setActiveModule(key as ModuleId)} /></nav>
            {!collapsed && <div className="file-toolbar"><Space size="small" className="file-toolbar-space">{activeModule !== "reference" && <Button aria-label="新建" type="primary" ghost icon={<FileAddOutlined />} disabled={!workspace} onClick={createFile}>新建</Button>}<Button aria-label="刷新" icon={<ReloadOutlined />} disabled={!workspace} onClick={() => refresh()}>刷新</Button></Space></div>}
            {!collapsed && <div className="file-list">{fileItems.length ? <Flex vertical gap={6}>{fileItems.map((path) => <Button type="text" block className={document?.path === path ? "file-item active" : "file-item"} key={path} onClick={() => openFile(path)}><Flex vertical gap={2} align="start"><Typography.Text strong ellipsis>{path.split("/").at(-2) === "deck" || path.split("/").at(-2) === "style" || path.split("/").at(-2) === "emote" ? `${path.split("/").at(-2)} / ${path.split("/").at(-1)}` : path.split("/").at(-1)}</Typography.Text><Typography.Text type="secondary" ellipsis>{path}</Typography.Text></Flex></Button>)}</Flex> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={workspace ? "此模块没有文件" : "请先打开工作区"} />}</div>}
          </Sider>
          <Content className="workspace-main">
            <div className="document-toolbar"><div><Breadcrumb items={pathParts.map((part, index) => ({ title: index === pathParts.length - 1 && document?.dirty ? <Space size={6}><span>{part}</span><Tag color="orange">未保存</Tag></Space> : part }))} /></div><Space size="small">{document && document.module !== "reference" && <><Button icon={<CopyOutlined />} onClick={duplicateFile}>复制</Button><Button icon={<EditOutlined />} onClick={renameFile}>重命名</Button><Button danger icon={<DeleteOutlined />} onClick={deleteFile}>删除</Button><Button type="primary" icon={<SaveOutlined />} loading={busy} disabled={hasErrors || !document.dirty} onClick={save}>保存</Button></>}</Space></div>
            {document && document.module !== "reference" && <ValidationPanel issues={issues} />}
            <Spin spinning={busy} description="处理中..." className="editor-spin"><div className="scroll-area">{renderEditor()}</div></Spin>
          </Content>
        </Layout>
      </Layout>
      <Modal title={textDialog?.title} open={!!textDialog} okText="确定" cancelText="取消" onOk={() => finishTextDialog(textDialogValue.trim() || null)} onCancel={() => finishTextDialog(null)}>
        <Form layout="vertical"><Form.Item label={textDialog?.label} required><Input autoFocus value={textDialogValue} placeholder={textDialog?.placeholder} onChange={(event) => setTextDialogValue(event.target.value)} onPressEnter={() => finishTextDialog(textDialogValue.trim() || null)} /></Form.Item></Form>
      </Modal>
      <Modal title={confirmDialog?.title} open={!!confirmDialog} okText={confirmDialog?.okText ?? "继续"} cancelText="取消" okButtonProps={{ danger: confirmDialog?.danger }} onOk={() => finishConfirmDialog(true)} onCancel={() => finishConfirmDialog(false)}>{confirmDialog?.content}</Modal>
    </AntdApp>
  </ConfigProvider>;
}
