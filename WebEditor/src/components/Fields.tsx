import { useState, type KeyboardEvent, type ReactNode } from "react";
import { ArrowDownOutlined, ArrowUpOutlined, CopyOutlined, DeleteOutlined, EditOutlined } from "@ant-design/icons";
import { Alert, Button, Card as AntCard, Checkbox, Collapse, Form, Input, InputNumber, Modal, Popover, Select, Space, Switch, Tag, Tooltip, Typography } from "antd";
import type { CardEntry } from "../types";
import { baseCardId, cardSummary, isCustomCardId, normalizeCardId } from "../data/cards";
import { defaultSkillDslGroup, dslKeyColors, formatSkillDsl, parseSkillDsl, type SkillDslBlock, type SkillDslGroup } from "../models/skillDsl";
import { useCardEntry } from "./CardCatalog";

export const cardPortalUrl = (cardId: number | string) => `https://shadowverse-portal.com/card/${encodeURIComponent(String(cardId))}?lang=zh-tw`;
export const cardImageUrl = (cardId: number) => `https://svgdb.me/assets/cards/jp/C_${cardId}.png`;

/** What to call a card the bundled catalog does not list. */
function unknownCardLabel(cardId: number) {
  return isCustomCardId(cardId) ? "自制卡牌" : "未知卡牌";
}

/** Secondary line under a card ID input: the resolved name plus its stats. */
function cardHint(entry: CardEntry | undefined, cardId: number | string) {
  const id = normalizeCardId(cardId);
  if (id == null) return undefined;
  if (entry) return `${entry.name} · ${cardSummary(entry)}`;
  return isCustomCardId(id) ? "自制卡牌 · 内置卡表不含此 ID" : "未知卡牌 · 内置卡表中没有此 ID";
}

/** Card name for inline use in chips, list rows and card titles. */
export function CardNameLabel({ cardId }: { cardId: number | string }) {
  const entry = useCardEntry(cardId);
  const id = normalizeCardId(cardId);
  if (id == null) return <span className="card-name-label card-name-label-empty">未设置</span>;
  if (entry) return <span className="card-name-label" title={`${entry.name} · ${cardSummary(entry)}`}>{entry.name}</span>;
  return <span className={`card-name-label ${isCustomCardId(id) ? "card-name-label-custom" : "card-name-label-unknown"}`}>{unknownCardLabel(id)}</span>;
}

/** Plain-string card name for titles and subtitles that cannot host an element. */
export function cardTitle(entry: CardEntry | undefined, cardId: number | string) {
  const id = normalizeCardId(cardId);
  if (id == null) return "未设置卡牌";
  return `${entry ? entry.name : unknownCardLabel(id)} #${id}`;
}

function CardIdPopoverContent({ cardId, url }: { cardId: number; url: string }) {
  const normalId = baseCardId(cardId);
  const entry = useCardEntry(cardId);
  return <div className="card-id-preview-card">
    <div className="card-id-preview-heading">
      <Typography.Text strong>{entry ? entry.name : unknownCardLabel(cardId)}</Typography.Text>
      <Typography.Text type="secondary">{entry ? cardSummary(entry) : "内置卡表中没有此 ID"}</Typography.Text>
      <Typography.Text type="secondary" code>#{cardId}</Typography.Text>
    </div>
    <a className="card-id-preview-link" href={url} target="_blank" rel="noreferrer">
      <img className="card-id-preview" src={cardImageUrl(normalId)} alt={`Card ${normalId}`} />
    </a>
  </div>;
}

export function CardIdTooltip({ cardId, children, block = false }: { cardId: number | string; children: ReactNode; block?: boolean }) {
  const normalized = normalizeCardId(cardId);
  if (normalized == null) return <>{children}</>;
  const url = cardPortalUrl(normalized);
  return <Popover
    trigger="hover"
    placement="topLeft"
    mouseEnterDelay={0.25}
    content={<CardIdPopoverContent cardId={normalized} url={url} />}
  >
    <span className={`card-id-tooltip-trigger${block ? " card-id-tooltip-trigger-block" : ""}`}>{children}</span>
  </Popover>;
}


export function Field({ label, field, hint, children, wide = false }: { label: string; field?: string; hint?: string; children: ReactNode; wide?: boolean }) {
  return <Form.Item layout="vertical" className={`field ${wide ? "field-wide" : ""}`} label={<span className="field-label">{label}{field && <code>{field}</code>}</span>}>
    {children}
    {hint && <Typography.Text type="secondary" className="field-hint">{hint}</Typography.Text>}
  </Form.Item>;
}

export function TextField({ label, field, value, onChange, multiline = false, placeholder, wide, disabled }: { label: string; field?: string; value: string; onChange: (value: string) => void; multiline?: boolean; placeholder?: string; wide?: boolean; disabled?: boolean }) {
  return <Field label={label} field={field} wide={wide}>
    {multiline ? <Input.TextArea value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} disabled={disabled} rows={4} /> : <Input value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} disabled={disabled} />}
  </Field>;
}

export { formatSkillDsl, parseSkillDsl } from "../models/skillDsl";
export type { SkillDslBlock, SkillDslGroup, SkillDslParseResult } from "../models/skillDsl";

export function SkillDslField({ label, field, value, onChange, wide = true, hint }: { label: string; field?: string; value: string; onChange: (value: string) => void; wide?: boolean; hint?: string }) {
  const [open, setOpen] = useState(false);
  const [mode, setMode] = useState<"structured" | "raw">("structured");
  const [groups, setGroups] = useState<SkillDslGroup[]>([]);
  const [rawDraft, setRawDraft] = useState(value);
  const [parseError, setParseError] = useState("");
  const trimmedValue = value.trim();
  const lineCount = value ? value.split(/\r?\n/).length : 0;
  const structuredValue = formatSkillDsl(groups);
  const blockCount = groups.reduce((count, group) => count + group.blocks.length, 0);
  const emptyKey = groups.some((group) => group.blocks.some((block) => !block.key.trim()));

  const beginEdit = () => {
    const parsed = parseSkillDsl(value);
    setGroups(parsed.groups);
    setRawDraft(value);
    setParseError(parsed.error ?? "");
    setMode(parsed.error ? "raw" : "structured");
    setOpen(true);
  };
  const enterRawMode = () => {
    if (mode === "structured") {
      setRawDraft(structuredValue);
      setParseError("");
    }
    setMode("raw");
  };
  const enterStructuredMode = () => {
    const parsed = parseSkillDsl(rawDraft);
    if (parsed.error) { setParseError(parsed.error); return; }
    setGroups(parsed.groups);
    setParseError("");
    setMode("structured");
  };
  const updateBlock = (groupIndex: number, blockIndex: number, patch: Partial<SkillDslBlock>) => setGroups((current) => current.map((group, currentGroup) => currentGroup !== groupIndex ? group : {
    ...group,
    blocks: group.blocks.map((block, currentBlock) => currentBlock !== blockIndex ? block : { ...block, ...patch }),
  }));
  const removeBlock = (groupIndex: number, blockIndex: number) => setGroups((current) => current.map((group, currentGroup) => currentGroup !== groupIndex ? group : { ...group, blocks: group.blocks.filter((_, currentBlock) => currentBlock !== blockIndex) }));
  const removeGroup = (groupIndex: number) => setGroups((current) => current.filter((_, currentGroup) => currentGroup !== groupIndex));
  const addGroup = () => setGroups((current) => [...current, defaultSkillDslGroup()]);
  const addBlock = (groupIndex: number) => setGroups((current) => current.map((group, currentGroup) => currentGroup !== groupIndex ? group : { ...group, blocks: [...group.blocks, { key: "custom", value: "" }] }));
  const apply = () => {
    if (mode === "structured" && emptyKey) return;
    onChange(mode === "structured" ? structuredValue : rawDraft);
    setOpen(false);
  };
  const handleShortcut = (event: KeyboardEvent<HTMLElement>) => {
    if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
      event.preventDefault();
      apply();
    }
  };

  return <>
    <Field label={label} field={field} wide={wide} hint={hint}>
      <button type="button" className="dsl-editor-trigger" aria-label={`编辑${label}`} onClick={beginEdit}>
        <span className="dsl-editor-trigger-copy">
          <span className={`dsl-editor-preview${trimmedValue ? "" : " dsl-editor-preview-empty"}`}>
            {trimmedValue || "尚未设置技能 DSL，点击此处开始编辑"}
          </span>
          <span className="dsl-editor-meta">{value ? `${value.length} 字符 · ${lineCount} 行` : "空 DSL"}</span>
        </span>
        <span className="dsl-editor-trigger-action"><EditOutlined /> 编辑</span>
      </button>
    </Field>
    <Modal
      className="dsl-editor-modal"
      open={open}
      title={<span className="dsl-editor-modal-title"><EditOutlined /> {label}</span>}
      width={980}
      centered
      okText="应用修改"
      cancelText="取消"
      okButtonProps={{ disabled: mode === "structured" && emptyKey }}
      onCancel={() => setOpen(false)}
      onOk={apply}
    >
      <div className="dsl-editor-help">
        <Typography.Text type="secondary">按语法块编辑；复杂或自定义内容可切换到原始模式。按 </Typography.Text>
        <Typography.Text code>Ctrl/Cmd + Enter</Typography.Text>
        <Typography.Text type="secondary"> 可快速应用。</Typography.Text>
        {field && <Typography.Text type="secondary" className="dsl-editor-field-hint">保存字段：<code>{field}</code></Typography.Text>}
      </div>
      <div className="dsl-editor-mode-bar">
        <Space.Compact>
          <Button type={mode === "structured" ? "primary" : "default"} onClick={enterStructuredMode}>结构化编辑</Button>
          <Button type={mode === "raw" ? "primary" : "default"} onClick={enterRawMode}>原始 DSL</Button>
        </Space.Compact>
        <Typography.Text type="secondary">{mode === "structured" ? `${groups.length} 个技能组 · ${blockCount} 个字段块` : "保留原始文本格式"}</Typography.Text>
      </div>
      {parseError && <Alert className="dsl-editor-alert" type="warning" showIcon message="当前内容无法安全拆分为结构化字段" description={`${parseError} 已保留原始文本，可直接修正后再切回结构化模式。`} />}
      {mode === "structured" ? <div className="dsl-structured-editor">
        {!groups.length && <div className="dsl-editor-empty"><Typography.Text type="secondary">暂无技能组。新增后会自动生成常用的 Skill、Timing、Condition、Target、Option、Preprocess 字段。</Typography.Text><Button type="primary" onClick={addGroup}>新增技能组</Button></div>}
        {!!groups.length && <div className="dsl-group-list">{groups.map((group, groupIndex) => <div className="dsl-group-card" key={groupIndex}>
          <div className="dsl-group-heading"><Space><Tag color={groupIndex % 2 ? "green" : "blue"}>技能组 {groupIndex + 1}</Tag><Typography.Text type="secondary">{group.blocks.length} 个字段块</Typography.Text></Space><Button type="text" danger onClick={() => removeGroup(groupIndex)}>删除技能组</Button></div>
          <div className="dsl-block-list">{group.blocks.map((block, blockIndex) => <div className="dsl-block-card" key={blockIndex}>
            <div className="dsl-block-heading"><span className="dsl-block-order">{blockIndex + 1}</span><Tag color={dslKeyColors[block.key] ?? "default"}>{block.key || "未命名字段"}</Tag><Input aria-label={`技能组 ${groupIndex + 1} 字段 ${blockIndex + 1} 名称`} value={block.key} placeholder="字段名，例如 skill" onChange={(event) => updateBlock(groupIndex, blockIndex, { key: event.target.value })} onKeyDown={handleShortcut} /><Button type="text" danger onClick={() => removeBlock(groupIndex, blockIndex)}>删除</Button></div>
            <Input.TextArea aria-label={`技能组 ${groupIndex + 1} 字段 ${blockIndex + 1} 值`} className="dsl-block-value" value={block.value} placeholder="字段值；可包含嵌套括号、条件表达式或 & 参数" autoSize={{ minRows: 2, maxRows: 8 }} spellCheck={false} onChange={(event) => updateBlock(groupIndex, blockIndex, { value: event.target.value })} onKeyDown={handleShortcut} />
            <Typography.Text code className="dsl-block-preview">({block.key || "字段名"}:{block.value || "字段值"})</Typography.Text>
          </div>)}</div>
          <Button type="dashed" className="dsl-add-block" onClick={() => addBlock(groupIndex)}>添加 DSL 字段块</Button>
        </div>)}</div>}
        <Button type="dashed" className="dsl-add-group" onClick={addGroup}>新增技能组</Button>
        {!!groups.length && <div className="dsl-format-preview"><Typography.Text strong>生成预览</Typography.Text><pre>{structuredValue || "（空 DSL）"}</pre></div>}
      </div> : <Input.TextArea aria-label={`${label} 原始编辑区`} className="dsl-editor-textarea" value={rawDraft} autoFocus spellCheck={false} autoSize={{ minRows: 16, maxRows: 32 }} onChange={(event) => setRawDraft(event.target.value)} onKeyDown={handleShortcut} />}
      <div className="dsl-editor-counter">{mode === "structured" ? `${structuredValue.length} 字符 · ${groups.length} 个技能组` : `${rawDraft.length} 字符 · ${rawDraft ? rawDraft.split(/\r?\n/).length : 0} 行`}</div>
    </Modal>
  </>;
}

export function NumberField({ label, field, value, onChange, min, max, disabled, cardId = false }: { label: string; field?: string; value: number; onChange: (value: number) => void; min?: number; max?: number; disabled?: boolean; cardId?: boolean }) {
  const entry = useCardEntry(cardId ? value : null);
  const input = <InputNumber className="full-number" value={Number.isFinite(value) ? value : 0} min={min} max={max} disabled={disabled} onChange={(item) => onChange(item ?? 0)} />;
  return <Field label={label} field={field} hint={cardId ? cardHint(entry, value) : undefined}>
    {cardId ? <CardIdTooltip cardId={value} block>{input}</CardIdTooltip> : input}
  </Field>;
}

export function NullableNumberField({ label, field, value, onChange, min }: { label: string; field?: string; value: number | null; onChange: (value: number | null) => void; min?: number }) {
  return <Field label={label} field={field}>
    <div className="nullable-input">
      <Checkbox checked={value == null} onChange={(event) => onChange(event.target.checked ? null : min ?? 0)}>无限制</Checkbox>
      <InputNumber className="full-number" value={value ?? undefined} min={min} disabled={value == null} onChange={(item) => onChange(item)} />
    </div>
  </Field>;
}

export function CheckboxField({ label, field, value, onChange, disabled }: { label: string; field?: string; value: boolean; onChange: (value: boolean) => void; disabled?: boolean }) {
  return <Field label={label} field={field}>
    <Space size={8}><Switch checked={value} disabled={disabled} onChange={onChange} checkedChildren="启用" unCheckedChildren="禁用" /><Typography.Text type="secondary">{value ? "已启用" : "已禁用"}</Typography.Text></Space>
  </Field>;
}

export function SelectField({ label, field, value, onChange, options }: { label: string; field?: string; value: string | number; onChange: (value: string) => void; options: { value: string | number; label: string }[] }) {
  return <Field label={label} field={field}>
    <Select value={String(value)} onChange={onChange} options={options.map((option) => ({ ...option, value: String(option.value) }))} />
  </Field>;
}

export function Section({ title, description, actions, children, collapsible = false, defaultOpen = true }: { title: string; description?: string; actions?: ReactNode; children: ReactNode; collapsible?: boolean; defaultOpen?: boolean }) {
  const heading = <span className="section-heading"><Typography.Text strong>{title}</Typography.Text>{description && <Typography.Text type="secondary">{description}</Typography.Text>}</span>;
  if (collapsible) return <Collapse className="section section-collapse" defaultActiveKey={defaultOpen ? ["content"] : []} items={[{ key: "content", label: heading, extra: actions, children }]} />;
  return <AntCard className="section" title={heading} extra={actions} size="small">{children}</AntCard>;
}

export function Card({ title, subtitle, actions, children }: { title: string; subtitle?: string; actions?: ReactNode; children: ReactNode }) {
  return <AntCard className="editor-card" title={<span className="section-heading"><Typography.Text strong>{title}</Typography.Text>{subtitle && <Typography.Text type="secondary">{subtitle}</Typography.Text>}</span>} extra={actions} size="small">{children}</AntCard>;
}

export function CollapsibleCard({ title, subtitle, actions, children, defaultOpen = false }: { title: string; subtitle?: string; actions?: ReactNode; children: ReactNode; defaultOpen?: boolean }) {
  const heading = <span className="section-heading"><Typography.Text strong>{title}</Typography.Text>{subtitle && <Typography.Text type="secondary">{subtitle}</Typography.Text>}</span>;
  return <Collapse className="editor-collapse-card" defaultActiveKey={defaultOpen ? ["content"] : []} items={[{ key: "content", label: heading, extra: actions ? <span onClick={(event) => event.stopPropagation()}>{actions}</span> : undefined, children }]} />;
}

export function RowActions({ index, count, onMove, onCopy, onDelete }: { index: number; count: number; onMove: (from: number, to: number) => void; onCopy?: () => void; onDelete: () => void }) {
  return <Space.Compact size="small">
    <Tooltip title="上移"><Button aria-label="上移" icon={<ArrowUpOutlined />} disabled={index === 0} onClick={() => onMove(index, index - 1)} /></Tooltip>
    <Tooltip title="下移"><Button aria-label="下移" icon={<ArrowDownOutlined />} disabled={index === count - 1} onClick={() => onMove(index, index + 1)} /></Tooltip>
    {onCopy && <Tooltip title="复制"><Button aria-label="复制" icon={<CopyOutlined />} onClick={onCopy} /></Tooltip>}
    <Tooltip title="删除"><Button aria-label="删除" danger icon={<DeleteOutlined />} onClick={onDelete} /></Tooltip>
  </Space.Compact>;
}

export function moveItem<T>(items: T[], from: number, to: number) {
  const result = [...items];
  const [item] = result.splice(from, 1);
  result.splice(to, 0, item);
  return result;
}
