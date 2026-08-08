import { useEffect, useState } from "react";
import { CopyOutlined, DeleteOutlined, PlusOutlined } from "@ant-design/icons";
import { Button, Input, Space, Table, Tag, Typography } from "antd";
import type { ColumnsType } from "antd/es/table";
import type { CsvDocument } from "../types";
import { addDeckTag, deckBaseHeaders, emoteHeaders, normalizeDeckCsv, styleHeaders } from "../models/csv";
import { CardIdTooltip, Field, Section } from "../components/Fields";

type AiType = "deck" | "style" | "emote";

function updateRow(document: CsvDocument, rowIndex: number, field: string, value: string): CsvDocument {
  const rows = [...document.rows];
  rows[rowIndex] = { ...rows[rowIndex], [field]: value };
  return { ...document, rows };
}

function rowKey(_: Record<string, string>, index?: number) { return String(index ?? 0); }

const tagColors = ["blue", "green", "gold", "purple", "cyan", "magenta", "orange", "geekblue"] as const;

function SimpleCsvTable({ document, headers, onChange }: { document: CsvDocument; headers: string[]; onChange: (value: CsvDocument) => void }) {
  const extras = document.headers.filter((header) => !headers.includes(header));
  const allHeaders = [...headers, ...extras];
  const columns: ColumnsType<Record<string, string>> = [
    { title: "#", key: "index", width: 52, fixed: "left", render: (_, __, rowIndex) => rowIndex + 1 },
    ...allHeaders.map((header) => ({ title: header, key: header, dataIndex: header, width: 150, render: (value: string, _: Record<string, string>, rowIndex: number) => { const input = <Input bordered={false} value={value ?? ""} onChange={(event) => onChange(updateRow(document, rowIndex, header, event.target.value))} />; return /^card[_-]?id$/i.test(header.trim()) ? <CardIdTooltip cardId={value}>{input}</CardIdTooltip> : input; } })),
    { title: "操作", key: "actions", width: 62, fixed: "right", render: (_: unknown, __: Record<string, string>, rowIndex: number) => <Button aria-label={`删除第 ${rowIndex + 1} 行`} danger type="text" icon={<DeleteOutlined />} onClick={() => onChange({ ...document, rows: document.rows.filter((_, index) => index !== rowIndex) })} /> },
  ];
  return <Space direction="vertical" size="middle" className="table-editor"><Table<Record<string, string>> size="small" bordered sticky pagination={{ pageSize: 50, showSizeChanger: true, pageSizeOptions: [20, 50, 100] }} scroll={{ x: Math.max(900, allHeaders.length * 150) }} rowKey={rowKey} columns={columns} dataSource={document.rows} /><Button icon={<PlusOutlined />} onClick={() => onChange({ ...document, headers: allHeaders, rows: [...document.rows, Object.fromEntries(allHeaders.map((header) => [header, ""]))] })}>新增行</Button></Space>;
}

function DeckTable({ document: sourceDocument, onChange }: { document: CsvDocument; onChange: (value: CsvDocument) => void }) {
  const document = normalizeDeckCsv(sourceDocument);
  const tagIndexes = [...new Set(document.headers.flatMap((header) => /^Tag(\d+)\.(?:Type|Arg|Condition)$/i.exec(header)?.[1] ? [Number(/^Tag(\d+)\.(?:Type|Arg|Condition)$/i.exec(header)![1])] : []))].sort((a, b) => a - b);
  type DeckTagRow = { key: string; tagIndex: number; type: string; arg: string; condition: string };
  const tagEditor = (row: Record<string, string>, rowIndex: number) => {
    const tagRows: DeckTagRow[] = tagIndexes.map((tagIndex) => ({ key: `${rowIndex}-${tagIndex}`, tagIndex, type: row[`Tag${tagIndex}.Type`] ?? "", arg: row[`Tag${tagIndex}.Arg`] ?? "", condition: row[`Tag${tagIndex}.Condition`] ?? "" }));
    const updateTag = (tagIndex: number, field: "Type" | "Arg" | "Condition", value: string) => onChange(updateRow(document, rowIndex, `Tag${tagIndex}.${field}`, value));
    const tagColumns: ColumnsType<DeckTagRow> = [
      { title: "所属主数据", key: "parent", width: 230, render: () => <div className="ai-tag-parent-cell"><Tag color="blue">第 {rowIndex + 1} 行</Tag><Typography.Text strong ellipsis>{row.CardName || "未命名卡牌"}</Typography.Text><Typography.Text type="secondary" ellipsis>{row.CardID || "无 CardID"}</Typography.Text></div> },
      { title: "Tag", key: "tag", width: 80, render: (_: unknown, tagRow) => <Tag color={tagColors[(tagRow.tagIndex - 1) % tagColors.length]}>Tag</Tag> },
      { title: "Type", key: "type", dataIndex: "type", width: 190, render: (value: string, tagRow) => <Input value={value} placeholder="Type" onChange={(event) => updateTag(tagRow.tagIndex, "Type", event.target.value)} /> },
      { title: "Arg", key: "arg", dataIndex: "arg", width: 280, render: (value: string, tagRow) => <Input value={value} placeholder="Arg" onChange={(event) => updateTag(tagRow.tagIndex, "Arg", event.target.value)} /> },
      { title: "Condition", key: "condition", dataIndex: "condition", width: 280, render: (value: string, tagRow) => <Input value={value} placeholder="Condition" onChange={(event) => updateTag(tagRow.tagIndex, "Condition", event.target.value)} /> },
    ];
    return <div className="ai-tag-panel"><div className="ai-tag-panel-heading"><Tag color="blue">主数据第 {rowIndex + 1} 行</Tag><Typography.Text strong>{row.CardName || "未命名卡牌"}</Typography.Text><Typography.Text type="secondary">CardID: {row.CardID || "-"} · 以下每行对应一个 Tag</Typography.Text></div><Table<DeckTagRow> className="ai-tag-table" size="small" bordered pagination={false} rowKey="key" columns={tagColumns} dataSource={tagRows} rowClassName={(tagRow) => `ai-tag-index-${(tagRow.tagIndex - 1) % tagColors.length}`} scroll={{ x: 1060 }} /></div>;
  };
  const columns: ColumnsType<Record<string, string>> = [
    { title: "#", key: "index", width: 52, fixed: "left", render: (_, __, rowIndex) => rowIndex + 1 },
    ...deckBaseHeaders.map((header) => ({ title: header, key: header, dataIndex: header, width: 150, render: (value: string, _: Record<string, string>, rowIndex: number) => { const input = <Input bordered={false} value={value ?? ""} onChange={(event) => onChange(updateRow(document, rowIndex, header, event.target.value))} />; return /^card[_-]?id$/i.test(header.trim()) ? <CardIdTooltip cardId={value}>{input}</CardIdTooltip> : input; } })),
    { title: "Tags", key: "tags", width: 90, render: (_: unknown, row: Record<string, string>) => <Tag color="blue">{tagIndexes.filter((index) => row[`Tag${index}.Type`]).length} 个</Tag> },
    { title: "操作", key: "actions", width: 104, fixed: "right", render: (_: unknown, row: Record<string, string>, rowIndex: number) => <Space.Compact><Button aria-label="复制" icon={<CopyOutlined />} onClick={() => onChange({ ...document, rows: [...document.rows.slice(0, rowIndex + 1), { ...row }, ...document.rows.slice(rowIndex + 1)] })} /><Button aria-label="删除" danger icon={<DeleteOutlined />} onClick={() => onChange({ ...document, rows: document.rows.filter((_, index) => index !== rowIndex) })} /></Space.Compact> },
  ];
  return <Space direction="vertical" size="middle" className="table-editor"><Table<Record<string, string>> size="small" bordered sticky pagination={{ pageSize: 50, showSizeChanger: true, pageSizeOptions: [20, 50, 100] }} scroll={{ x: Math.max(1200, deckBaseHeaders.length * 150) }} rowKey={rowKey} columns={columns} dataSource={document.rows} expandable={{ expandedRowRender: (row, index) => tagEditor(row, index ?? 0), rowExpandable: () => tagIndexes.length > 0, expandRowByClick: true }} /><Space><Button icon={<PlusOutlined />} onClick={() => onChange({ ...document, rows: [...document.rows, Object.fromEntries(document.headers.map((header) => [header, ""]))] })}>新增卡牌行</Button><Button onClick={() => onChange(addDeckTag(document))}>新增 Tag 列组</Button></Space></Space>;
}

export function AiDataEditor({ type, value, onChange }: { type: AiType; value: CsvDocument; onChange: (value: CsvDocument) => void }) {
  const headers = type === "style" ? styleHeaders : emoteHeaders;
  return <div className="editor-content"><Section title={type === "deck" ? "Deck AI" : type === "style" ? "Style AI" : "Emote AI"} description={`${value.rows.length} 行 · ${value.headers.length} 列`}>{type === "deck" ? <DeckTable document={value} onChange={onChange} /> : <SimpleCsvTable document={value} headers={headers} onChange={onChange} />}</Section></div>;
}

export function ReferenceViewer({ text, path }: { text: string; path: string }) {
  const [query, setQuery] = useState("");
  const [preview, setPreview] = useState(() => text.slice(0, 200_000));
  useEffect(() => {
    if (typeof Worker === "undefined") {
      const normalized = query.trim().toLocaleLowerCase();
      setPreview(normalized ? text.split(/\r?\n/).filter((line) => line.toLocaleLowerCase().includes(normalized)).slice(0, 500).join("\n") : text.slice(0, 200_000));
      return;
    }
    const worker = new Worker(new URL("../workers/referenceSearch.worker.ts", import.meta.url), { type: "module" });
    worker.onmessage = (event: MessageEvent<string>) => setPreview(event.data);
    worker.postMessage({ text, query });
    return () => worker.terminate();
  }, [text, query]);
  return <div className="editor-content"><Section title="只读 AI 参考" description={path}><Field label="搜索" wide><Input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="输入卡牌 ID、Tag 或文本" /></Field><pre className="reference-viewer">{preview}</pre>{text.length > 200_000 && !query && <Typography.Text type="secondary" className="field-hint">大型文件默认只显示前 200 KB；输入搜索词可筛选完整内容。</Typography.Text>}</Section></div>;
}
