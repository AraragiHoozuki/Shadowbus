import { useEffect, useState } from "react";
import { DeleteOutlined, PlusOutlined } from "@ant-design/icons";
import { Button, Input, InputNumber, Space, Typography } from "antd";
import { Card, Field, Section } from "./Fields";

function parseNumbers(text: string) {
  return text.split(/[\s,;，；]+/).map(Number).filter((value) => Number.isFinite(value));
}

export function NumberListEditor({ label, field, value, onChange, max, hint }: { label: string; field?: string; value: number[]; onChange: (value: number[]) => void; max?: number; hint?: string }) {
  const [bulk, setBulk] = useState("");
  return <Section title={label} description={`${field ?? "列表"} · ${value.length} 项`} collapsible defaultOpen={false}>
    {hint && <Typography.Text type="secondary" className="field-hint">{hint}</Typography.Text>}
    <Space wrap className="chip-list">
      {value.map((item, index) => <Space.Compact className="chip" key={`${index}-${item}`}><InputNumber value={item} onChange={(next) => { const updated = [...value]; updated[index] = next ?? 0; onChange(updated); }} /><Button aria-label={`删除第 ${index + 1} 项`} danger icon={<DeleteOutlined />} onClick={() => onChange(value.filter((_, itemIndex) => itemIndex !== index))} /></Space.Compact>)}
    </Space>
    <Space.Compact className="bulk-row">
      <Input.TextArea value={bulk} onChange={(event) => setBulk(event.target.value)} placeholder="批量粘贴 ID，以逗号、空格或换行分隔" autoSize={{ minRows: 1, maxRows: 3 }} />
      <Button type="primary" onClick={() => { const next = [...value, ...parseNumbers(bulk)]; onChange(max ? next.slice(0, max) : next); setBulk(""); }}>追加</Button>
      <Button onClick={() => onChange([...new Set(value)])}>去重</Button>
      <Button danger onClick={() => onChange([])}>清空</Button>
    </Space.Compact>
  </Section>;
}

export function StringListEditor({ label, field, value, onChange, multiline = true }: { label: string; field?: string; value: string[]; onChange: (value: string[]) => void; multiline?: boolean }) {
  return <Section title={label} description={`${field ?? "列表"} · ${value.length} 项`} collapsible defaultOpen={false}>
    <div className="stack">
      {value.map((item, index) => <Space.Compact className="list-row" key={index}>{multiline ? <Input.TextArea autoSize={{ minRows: 2, maxRows: 6 }} value={item} onChange={(event) => { const next = [...value]; next[index] = event.target.value; onChange(next); }} /> : <Input value={item} onChange={(event) => { const next = [...value]; next[index] = event.target.value; onChange(next); }} />}<Button danger icon={<DeleteOutlined />} onClick={() => onChange(value.filter((_, itemIndex) => itemIndex !== index))}>删除</Button></Space.Compact>)}
    </div>
    <Button icon={<PlusOutlined />} onClick={() => onChange([...value, ""])}>新增项目</Button>
  </Section>;
}

export function StringMapEditor({ label, field, value, onChange, valueMultiline = false }: { label: string; field?: string; value: Record<string, string>; onChange: (value: Record<string, string>) => void; valueMultiline?: boolean }) {
  const entries = Object.entries(value);
  const update = (index: number, key: string, itemValue: string) => onChange(Object.fromEntries(entries.map(([oldKey, oldValue], itemIndex) => itemIndex === index ? [key, itemValue] : [oldKey, oldValue])));
  return <Section title={label} description={`${field ?? "字典"} · ${entries.length} 项`} collapsible defaultOpen={false}>
    <div className="stack">{entries.map(([key, itemValue], index) => <Space.Compact className="map-row" key={`${index}-${key}`}><Input value={key} placeholder="字段名" onChange={(event) => update(index, event.target.value, itemValue)} />{valueMultiline ? <Input.TextArea autoSize={{ minRows: 2, maxRows: 6 }} value={itemValue} onChange={(event) => update(index, key, event.target.value)} /> : <Input value={itemValue} placeholder="值" onChange={(event) => update(index, key, event.target.value)} />}<Button danger icon={<DeleteOutlined />} onClick={() => onChange(Object.fromEntries(entries.filter((_, itemIndex) => itemIndex !== index)))}>删除</Button></Space.Compact>)}</div>
    <Button icon={<PlusOutlined />} onClick={() => onChange({ ...value, [`field_${entries.length + 1}`]: "" })}>新增字段</Button>
  </Section>;
}

export function NumberMapEditor({ label, field, value, onChange }: { label: string; field?: string; value: Record<string, number>; onChange: (value: Record<string, number>) => void }) {
  const entries = Object.entries(value);
  const update = (index: number, key: string, itemValue: number) => onChange(Object.fromEntries(entries.map(([oldKey, oldValue], itemIndex) => itemIndex === index ? [key, itemValue] : [oldKey, oldValue])));
  return <Section title={label} description={`${field ?? "字典"} · ${entries.length} 项`} collapsible defaultOpen={false}>
    <div className="stack">{entries.map(([key, itemValue], index) => <Space.Compact className="map-row" key={`${index}-${key}`}><Input value={key} placeholder="ID" onChange={(event) => update(index, event.target.value, itemValue)} /><InputNumber value={itemValue} onChange={(next) => update(index, key, next ?? 0)} /><Button danger icon={<DeleteOutlined />} onClick={() => onChange(Object.fromEntries(entries.filter((_, itemIndex) => itemIndex !== index)))}>删除</Button></Space.Compact>)}</div>
    <Button icon={<PlusOutlined />} onClick={() => onChange({ ...value, "0": 1 })}>新增项目</Button>
  </Section>;
}

export function UnknownFieldsEditor({ value, knownKeys, onChange }: { value: Record<string, unknown>; knownKeys: string[]; onChange: (next: Record<string, unknown>) => void }) {
  const unknown = Object.fromEntries(Object.entries(value).filter(([key]) => !knownKeys.includes(key)));
  const [text, setText] = useState(JSON.stringify(unknown, null, 2));
  const [error, setError] = useState("");
  useEffect(() => setText(JSON.stringify(unknown, null, 2)), [JSON.stringify(unknown)]);
  return <Card title="其他 / 未知字段" subtitle="保存时原样保留，用于兼容未来版本">
    <Field label="额外 JSON" wide><Input.TextArea rows={6} value={text} onChange={(event) => setText(event.target.value)} /></Field>
    {error && <Typography.Text type="danger" className="validation-error">{error}</Typography.Text>}
    <Button type="primary" onClick={() => { try { const parsed = JSON.parse(text) as Record<string, unknown>; const known = Object.fromEntries(Object.entries(value).filter(([key]) => knownKeys.includes(key))); onChange({ ...parsed, ...known }); setError(""); } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); } }}>应用额外字段</Button>
  </Card>;
}
