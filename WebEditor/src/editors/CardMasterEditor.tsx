import type { AttackEffectFields, CardMasterPatch } from "../types";
import { useMemo, useState, type ReactNode } from "react";
import { CheckOutlined, CloseOutlined, EditOutlined, ImportOutlined, PlusOutlined } from "@ant-design/icons";
import { Alert, Button, Input, Modal, Space, Tag, Tooltip, Typography } from "antd";
import { cardParameterFields, localizationKeys, skillParallelKeys } from "../data/catalog";
import { newCardPatch } from "../models/defaults";
import { skillRowsFromDsl } from "../models/cardDsl";
import { applySkillGroups, emptySkillRow, isSkillField, onlySkillFields, parseSkillGroups, type SkillFieldSource, type SkillGroups, type SkillRow } from "../models/skills";
import { Card, CheckboxField, Field, NumberField, RowActions, Section, TextField, cardTitle, moveItem } from "../components/Fields";
import { useCardCatalog } from "../components/CardCatalog";
import { StringMapEditor, UnknownFieldsEditor } from "../components/Collections";

const known = ["newCard", "cardId", "templateCardId", "boolFields", "intFields", "intArrayFields", "stringChangeFields", "stringAppendFields", "stringArrayFields", "localizationFields", "attackEffectFields"];

/** The patch map each write mode stores the six parallel skill fields in. */
const skillSourceMap = { append: "stringAppendFields", change: "stringChangeFields" } as const;

const skillTagColors = ["blue", "green", "gold", "purple", "cyan", "magenta", "orange", "geekblue"] as const;
const replacementTagColors = ["processing", "success", "warning", "purple", "cyan", "magenta", "orange", "geekblue"] as const;

function SkillTagMatrix({ groupLabel, rows, onChange, onMove, onCopy, onDelete }: { groupLabel: string; rows: SkillRow[]; onChange: (rows: SkillRow[]) => void; onMove: (from: number, to: number) => void; onCopy: (index: number) => void; onDelete: (index: number) => void }) {
  const [editingRow, setEditingRow] = useState<number | null>(null);
  const [draft, setDraft] = useState<SkillRow | null>(null);
  const beginEdit = (row: number) => { setEditingRow(row); setDraft({ ...rows[row] }); };
  const updateDraft = <K extends keyof SkillRow>(key: K, value: SkillRow[K]) => setDraft((current) => current ? { ...current, [key]: value } : current);
  const commitEdit = () => {
    if (editingRow == null || !draft) return;
    const next = [...rows];
    next[editingRow] = draft;
    onChange(next);
    setEditingRow(null);
    setDraft(null);
  };
  const cancelEdit = () => { setEditingRow(null); setDraft(null); };
  const removeRow = (index: number) => onDelete(index);
  return <div className="skill-tag-editor">
    <div className="skill-tag-legend"><Typography.Text type="secondary">技能序号：</Typography.Text>{rows.map((_, index) => <span key={index} className={`skill-index-label skill-index-${index % skillTagColors.length}`}>{groupLabel} {index + 1}</span>)}<Typography.Text type="secondary">字段名独立显示；点击技能值或“编辑”打开弹窗，关闭 Tag 删除整组并行技能。</Typography.Text></div>
    <div className="skill-tag-matrix">
      {skillParallelKeys.map((key) => <div className="skill-tag-row" key={key}>
        <div className="skill-tag-label"><Typography.Text strong>{key}</Typography.Text><Typography.Text type="secondary" code>{key}</Typography.Text></div>
        <div className="skill-tag-values">{rows.map((row, index) => {
          const color = skillTagColors[index % skillTagColors.length];
          return <Tooltip title="点击编辑整组技能" key={`${key}-${index}`}><Tag color={color} closable onClose={(event) => { event.preventDefault(); removeRow(index); }} onClick={() => beginEdit(index)} className="skill-value-tag">{row[key] || "（空）"}</Tag></Tooltip>;
        })}</div>
      </div>)}
    </div>
    <div className="skill-row-actions">{rows.map((row, index) => <Space key={index} size={4}><span className={`skill-index-label skill-index-${index % skillTagColors.length}`}>{groupLabel} {index + 1}</span><Button size="small" icon={<EditOutlined />} onClick={() => beginEdit(index)}>编辑</Button><Button size="small" disabled={index === 0} onClick={() => onMove(index, index - 1)}>上移</Button><Button size="small" disabled={index === rows.length - 1} onClick={() => onMove(index, index + 1)}>下移</Button><Button size="small" onClick={() => onCopy(index)}>复制</Button><Button size="small" danger onClick={() => removeRow(index)}>删除</Button></Space>)}</div>
    <Modal className="skill-row-modal" open={editingRow != null} title={`编辑${groupLabel} ${editingRow == null ? "" : editingRow + 1}`} width={820} centered okText="应用修改" cancelText="取消" onCancel={cancelEdit} onOk={commitEdit}>
      {draft && <div className="field-grid skill-row-modal-fields">{skillParallelKeys.map((key) => <Field key={key} label={key} field={key} wide={key === "Skill"}><Input.TextArea value={draft[key]} rows={key === "Skill" ? 5 : 3} autoSize={{ minRows: key === "Skill" ? 4 : 2, maxRows: 8 }} spellCheck={false} onChange={(event) => updateDraft(key, event.target.value)} /></Field>)}</div>}
    </Modal>
  </div>;
}

/**
 * Pastes bracket DSL — normally copied out of the card reference panel — into one
 * form's list. Parsed live so the preview shows exactly which rows would be
 * appended, because appending to the wrong form is the mistake this editor exists
 * to prevent.
 */
function SkillDslImport({ label, onImport }: { label: string; onImport: (rows: SkillRow[]) => void }) {
  const [open, setOpen] = useState(false);
  const [text, setText] = useState("");
  const parsed = useMemo(() => skillRowsFromDsl(text), [text]);
  const close = () => { setOpen(false); setText(""); };
  return <>
    <Button size="small" type="dashed" icon={<ImportOutlined />} onClick={() => setOpen(true)}>导入 DSL</Button>
    <Modal
      open={open}
      title={`导入 DSL 到${label}`}
      width={760}
      centered
      okText={parsed.rows.length ? `追加 ${parsed.rows.length} 个技能` : "追加技能"}
      cancelText="取消"
      okButtonProps={{ disabled: !parsed.rows.length }}
      onCancel={close}
      onOk={() => { onImport(parsed.rows); close(); }}
    >
      <div className="stack skill-dsl-import">
        <Typography.Text type="secondary">
          粘贴 <code>(skill:...)(timing:...)</code> 形式的括号 DSL，多个技能用逗号分隔。
          解析结果会<strong>追加</strong>到{label}现有技能之后，不会覆盖。
        </Typography.Text>
        <Input.TextArea
          autoFocus
          rows={4}
          spellCheck={false}
          value={text}
          placeholder="(skill:damage)(timing:on_play)(condition:none)(target:enemy_follower)(option:amount_2)(preprocess:none)"
          onChange={(event) => setText(event.target.value)}
        />
        {parsed.error && <Alert type="error" showIcon message="无法解析" description={parsed.error} />}
        {!!parsed.ignored.length && <Alert
          type="warning"
          showIcon
          message="以下字段不会被导入"
          description={<>{parsed.ignored.map((key) => <Tag key={key}>{key}</Tag>)}技能编辑器只管理六个并行字段。演出类字段（<code>SkillEffectPath</code> 等）需要在「字符串替换」中手动添加。</>}
        />}
        {parsed.rows.map((row, index) => <div className="skill-dsl-preview" key={index}>
          <Tag color="blue">{label} 第 {index + 1} 个</Tag>
          <dl>{skillParallelKeys.map((key) => <div key={key}><dt>{key}</dt><dd>{row[key] || "（空）"}</dd></div>)}</dl>
        </div>)}
      </div>
    </Modal>
  </>;
}

/**
 * One half of a card's skills. The two halves are edited separately because the
 * game keeps them apart: appending a skill to the wrong half silently makes it
 * evolution-only, which is exactly what a single flat list gets wrong.
 */
function SkillGroupPanel({ label, color, description, empty, rows, onChange, extra }: { label: string; color: string; description: string; empty: string; rows: SkillRow[]; onChange: (rows: SkillRow[]) => void; extra?: ReactNode }) {
  return <div className="skill-group">
    <div className="skill-group-heading">
      <Space size={8}><Tag color={color}>{label}</Tag><Typography.Text type="secondary">{rows.length} 个技能 · {description}</Typography.Text></Space>
      <Space size={4}>{extra}<SkillDslImport label={label} onImport={(imported) => onChange([...rows, ...imported])} /><Button size="small" type="dashed" icon={<PlusOutlined />} onClick={() => onChange([...rows, emptySkillRow()])}>新增技能</Button></Space>
    </div>
    {rows.length
      ? <SkillTagMatrix groupLabel={label} rows={rows} onChange={onChange} onMove={(from, to) => onChange(moveItem(rows, from, to))} onCopy={(index) => onChange([...rows.slice(0, index + 1), { ...rows[index] }, ...rows.slice(index + 1)])} onDelete={(index) => onChange(rows.filter((_, itemIndex) => itemIndex !== index))} />
      : <Typography.Text type="secondary">{empty}</Typography.Text>}
  </div>;
}


function StringReplacementTags({ value, suggestions, onChange }: { value: Record<string, string>; suggestions: string[]; onChange: (value: Record<string, string>) => void }) {
  const entries = Object.entries(value);
  const [editing, setEditing] = useState<{ entry: number; tag: number } | null>(null);
  const [draft, setDraft] = useState("");
  const [editingKey, setEditingKey] = useState<number | null>(null);
  const [draftKey, setDraftKey] = useState("");
  const beginTagEdit = (entry: number, tag: number, text: string) => { setEditing({ entry, tag }); setDraft(text); };
  const commitTagEdit = () => {
    if (!editing) return;
    const [key, raw] = entries[editing.entry];
    const tags = raw === "" ? [""] : raw.split(",");
    tags[editing.tag] = draft;
    onChange({ ...value, [key]: tags.join(",") });
    setEditing(null);
  };
  const removeTag = (entry: number, tag: number) => {
    const [key, raw] = entries[entry];
    const tags = raw === "" ? [""] : raw.split(",");
    const nextTags = tags.filter((_, index) => index !== tag);
    onChange({ ...value, [key]: nextTags.length ? nextTags.join(",") : "" });
  };
  const addTag = (entry: number) => {
    const [key, raw] = entries[entry];
    const tags = raw === "" ? [""] : raw.split(",");
    tags.push("");
    onChange({ ...value, [key]: tags.join(",") });
    setEditing({ entry, tag: tags.length - 1 });
    setDraft("");
  };
  const beginKeyEdit = (entry: number) => { setEditingKey(entry); setDraftKey(entries[entry]?.[0] ?? ""); };
  const commitKeyEdit = () => {
    if (editingKey == null || !draftKey.trim()) return;
    const [oldKey] = entries[editingKey];
    const next = entries.map(([key, raw], index) => index === editingKey ? [draftKey.trim(), raw] : [key, raw]);
    if (draftKey.trim() === oldKey) { setEditingKey(null); return; }
    onChange(Object.fromEntries(next));
    setEditingKey(null);
  };
  const removeField = (entry: number) => onChange(Object.fromEntries(entries.filter((_, index) => index !== entry)));
  const addField = () => {
    const key = suggestions.find((item) => !(item in value)) ?? `Field${entries.length + 1}`;
    onChange({ ...value, [key]: "" });
    setEditingKey(entries.length);
    setDraftKey(key);
  };
  return <Section title="字符串替换" description={`stringChangeFields · ${entries.length} 个字段；每个逗号分段都是一个 Tag。六个技能字段在上方的技能编辑器中编辑。`}>
    <datalist id="suggestions-string-change">{suggestions.map((item) => <option key={item} value={item} />)}</datalist>
    <div className="replacement-tag-editor">
      <div className="replacement-field-list">
        {entries.map(([key, raw], entryIndex) => {
          const tags = raw === "" ? [""] : raw.split(",");
          return <div className="replacement-field-row" key={`${entryIndex}-${key}`}>
            <div className="replacement-field-label">
              {editingKey === entryIndex
                ? <Space.Compact size="small"><Input list="suggestions-string-change" autoFocus value={draftKey} onChange={(event) => setDraftKey(event.target.value)} onPressEnter={commitKeyEdit} onKeyDown={(event) => { if (event.key === "Escape") setEditingKey(null); }} /><Button type="primary" icon={<CheckOutlined />} onClick={commitKeyEdit} /><Button icon={<CloseOutlined />} onClick={() => setEditingKey(null)} /></Space.Compact>
                : <Tooltip title="双击编辑字段名"><div className="replacement-field-name" onDoubleClick={() => beginKeyEdit(entryIndex)}><Typography.Text strong>{key}</Typography.Text><Typography.Text type="secondary" code>{key}</Typography.Text></div></Tooltip>}
              <Button size="small" type="text" danger icon={<CloseOutlined />} aria-label={`删除 ${key}`} onClick={() => removeField(entryIndex)} />
            </div>
            <div className="replacement-tag-list">
              {tags.map((tagText, tagIndex) => editing?.entry === entryIndex && editing.tag === tagIndex
                ? <Space.Compact size="small" className="replacement-edit" key={`${entryIndex}-${tagIndex}`}><Input autoFocus value={draft} placeholder="替换值" onChange={(event) => setDraft(event.target.value)} onPressEnter={commitTagEdit} onKeyDown={(event) => { if (event.key === "Escape") setEditing(null); }} /><Button type="primary" icon={<CheckOutlined />} onClick={commitTagEdit} /><Button icon={<CloseOutlined />} onClick={() => setEditing(null)} /></Space.Compact>
                : <Tooltip title="双击编辑 Tag" key={`${entryIndex}-${tagIndex}`}><Tag color={replacementTagColors[tagIndex % replacementTagColors.length]} closable onClose={(event) => { event.preventDefault(); removeTag(entryIndex, tagIndex); }} onDoubleClick={() => beginTagEdit(entryIndex, tagIndex, tagText)} className="replacement-tag"><span>{tagText || "（空）"}</span></Tag></Tooltip>)}
              <Button size="small" type="dashed" icon={<PlusOutlined />} onClick={() => addTag(entryIndex)}>添加 Tag</Button>
            </div>
          </div>;
        })}
      </div>
      {!entries.length && <Typography.Text type="secondary">暂无字符串替换。点击“新增替换”添加字段。</Typography.Text>}
      <Button icon={<PlusOutlined />} onClick={addField}>新增替换字段</Button>
    </div>
  </Section>;
}

function GenericFieldMap<T extends string | number | boolean>({ title, field, value, type, suggestions, onChange }: { title: string; field: string; value: Record<string, T>; type: "string" | "number" | "boolean"; suggestions: string[]; onChange: (value: Record<string, T>) => void }) {
  const entries = Object.entries(value);
  const update = (index: number, key: string, itemValue: T) => onChange(Object.fromEntries(entries.map(([oldKey, oldValue], itemIndex) => itemIndex === index ? [key, itemValue] : [oldKey, oldValue])) as Record<string, T>);
  return <Section title={title} description={`${field} · ${entries.length} 项`} collapsible defaultOpen={false}>
    <datalist id={`suggestions-${field}`}>{suggestions.map((item) => <option key={item} value={item} />)}</datalist>
    <div className="stack">{entries.map(([key, itemValue], index) => <div className="map-row" key={`${index}-${key}`}><input list={`suggestions-${field}`} value={key} placeholder="CardParameter 属性" onChange={(event) => update(index, event.target.value, itemValue)} />{type === "boolean" ? <select value={String(itemValue)} onChange={(event) => update(index, (key), (event.target.value === "true") as T)}><option value="true">true</option><option value="false">false</option></select> : <input type={type === "number" ? "number" : "text"} value={String(itemValue)} onChange={(event) => update(index, key, (type === "number" ? Number(event.target.value) : event.target.value) as T)} />}<button type="button" className="danger" onClick={() => onChange(Object.fromEntries(entries.filter((_, itemIndex) => itemIndex !== index)) as Record<string, T>)}>删除</button></div>)}</div>
    <button type="button" onClick={() => onChange({ ...value, [suggestions.find((item) => !(item in value)) ?? `Field${entries.length + 1}`]: (type === "boolean" ? false : type === "number" ? 0 : "") as T })}>新增属性</button>
  </Section>;
}

function AttackEffectEditor({ value, onChange }: { value: AttackEffectFields; onChange: (value: AttackEffectFields) => void }) {
  const updatePair = (field: keyof AttackEffectFields, index: number, next: string | number) => {
    const current = Array.isArray(value[field]) ? [...(value[field] as (string | number)[])] : [];
    while (current.length < 2) current.push(typeof next === "number" ? 0 : "");
    current[index] = next;
    onChange({ ...value, [field]: current });
  };
  const pairText = (field: keyof AttackEffectFields) => {
    const current = Array.isArray(value[field]) ? value[field] as string[] : [];
    return [current[0] ?? "", current[1] ?? ""];
  };
  const pairNumber = (field: keyof AttackEffectFields) => {
    const current = Array.isArray(value[field]) ? value[field] as number[] : [];
    return [Number.isFinite(Number(current[0])) ? Number(current[0]) : 0, Number.isFinite(Number(current[1])) ? Number(current[1]) : 0];
  };
  const textPair = (label: string, field: keyof AttackEffectFields, placeholder?: string) => {
    const values = pairText(field);
    return <Field label={label} field={`attackEffectFields.${String(field)}`}>
      <Space.Compact block><Input addonBefore="普通" value={values[0]} placeholder={placeholder} onChange={(event) => updatePair(field, 0, event.target.value)} /><Input addonBefore="进化" value={values[1]} placeholder={placeholder} onChange={(event) => updatePair(field, 1, event.target.value)} /></Space.Compact>
    </Field>;
  };
  const numberValues = pairNumber("time");
  return <Section title="攻击特效" description="AtkEffectParameter：普通 / 进化两套攻击演出数据" collapsible defaultOpen={false}>
    <div className="stack">
      {textPair("攻击特效路径", "effectPath", "例如 btl_attack_1")}
      {textPair("攻击音效路径", "se", "例如 se_btl_attack_1")}
      {textPair("移动类型", "moveType", "例如 DIRECT、LINEAR")}
      {textPair("特效引擎类型", "effectEnginType", "NONE / SHURIKEN / FLATOUT / SOLID")}
      <Field label="攻击特效时长" field="attackEffectFields.time">
        <Space.Compact block><Input type="number" addonBefore="普通" value={numberValues[0]} onChange={(event) => updatePair("time", 0, Number(event.target.value) || 0)} /><Input type="number" addonBefore="进化" value={numberValues[1]} onChange={(event) => updatePair("time", 1, Number(event.target.value) || 0)} /></Space.Compact>
      </Field>
    </div>
  </Section>;
}

function PatchForm({ value, onChange }: { value: CardMasterPatch; onChange: (value: CardMasterPatch) => void }) {
  const set = <K extends keyof CardMasterPatch>(key: K, item: CardMasterPatch[K]) => onChange({ ...value, [key]: item });
  // Replacement runs before appending, so when both maps carry skill fields the
  // replacement is the base and the one worth editing. The other stays visible
  // in an alert rather than silently disappearing behind this form.
  const inChange = skillParallelKeys.some((key) => key in value.stringChangeFields);
  const inAppend = skillParallelKeys.some((key) => key in value.stringAppendFields);
  const skillSource: SkillFieldSource = inChange ? "change" : "append";
  const skillMap = skillSourceMap[skillSource];
  const otherSkillFields = inChange && inAppend ? onlySkillFields(value.stringAppendFields) : null;
  const groups = parseSkillGroups(value[skillMap], skillSource);
  const setGroups = (next: SkillGroups) => set(skillMap, applySkillGroups(value[skillMap], next));
  const switchSkillSource = (next: SkillFieldSource) => {
    if (next === skillSource) return;
    const target = skillSourceMap[next];
    onChange({
      ...value,
      [skillMap]: applySkillGroups(value[skillMap], { ...groups, normal: [], evolved: [], hasEvolution: false }),
      // Appending without a leading comma would glue onto the template's last entry.
      [target]: applySkillGroups(value[target], { ...groups, leadingComma: next === "append" }),
    });
  };
  return <div className="stack">
    <Section title="补丁目标">
      <div className="field-grid">
        <CheckboxField label="创建新卡" field="newCard" value={value.newCard} onChange={(item) => set("newCard", item)} />
        <NumberField label="新卡 ID" field="cardId" value={value.cardId} disabled={!value.newCard} cardId onChange={(item) => set("cardId", item)} />
        <NumberField label="模板卡 ID" field="templateCardId" value={value.templateCardId} cardId onChange={(item) => set("templateCardId", item)} />
      </div>
    </Section>
    <GenericFieldMap title="布尔属性" field="boolFields" value={value.boolFields} type="boolean" suggestions={cardParameterFields.boolean} onChange={(item) => set("boolFields", item)} />
    <GenericFieldMap title="整数 / 枚举属性" field="intFields" value={value.intFields} type="number" suggestions={cardParameterFields.number} onChange={(item) => set("intFields", item)} />
    <Section title="整数 / 枚举数组属性" description="intArrayFields；Tribe 使用类型枚举的数值" collapsible defaultOpen={false}><div className="stack">{Object.entries(value.intArrayFields).map(([key, items], index, entries) => <div className="map-row" key={`${index}-${key}`}><input list="suggestions-int-array" value={key} onChange={(event) => set("intArrayFields", Object.fromEntries(entries.map(([oldKey, oldValue], itemIndex) => itemIndex === index ? [event.target.value, oldValue] : [oldKey, oldValue])))} /><textarea rows={2} value={items.join("\n")} onChange={(event) => set("intArrayFields", { ...value.intArrayFields, [key]: event.target.value.split(/\s+/).map(Number).filter(Number.isFinite) })} /><button type="button" className="danger" onClick={() => set("intArrayFields", Object.fromEntries(entries.filter((_, itemIndex) => itemIndex !== index)))}>删除</button></div>)}</div><datalist id="suggestions-int-array">{cardParameterFields.numberArray.map((item) => <option key={item} value={item} />)}</datalist><button type="button" onClick={() => set("intArrayFields", { ...value.intArrayFields, [cardParameterFields.numberArray.find((item) => !(item in value.intArrayFields)) ?? "Field"]: [] })}>新增数组</button></Section>
    <AttackEffectEditor value={value.attackEffectFields ?? {}} onChange={(item) => set("attackEffectFields", item)} />
    <StringReplacementTags value={Object.fromEntries(Object.entries(value.stringChangeFields).filter(([key]) => !isSkillField(key)))} suggestions={cardParameterFields.string.filter((key) => !isSkillField(key))} onChange={(item) => set("stringChangeFields", { ...onlySkillFields(value.stringChangeFields), ...item })} />
    <Section title="技能编辑器" description={`${skillMap} 中的六个并行技能字段 · 普通形态 ${groups.normal.length} 个 / 进化形态 ${groups.evolved.length} 个`}>
      {otherSkillFields && <Alert className="skill-source-alert" type="warning" showIcon message="技能字段同时写在两个 map 中" description={<>游戏先应用 <code>stringChangeFields</code> 再追加 <code>stringAppendFields</code>。下方编辑的是 <code>stringChangeFields</code>；另一份仍会生效：<pre>{Object.entries(otherSkillFields).map(([key, item]) => `${key}: ${item}`).join("\n")}</pre>建议合并到一处。</>} />}
      <Field label="写入方式" hint={skillSource === "append" ? "追加到模板卡已有技能之后。若模板卡本身带进化技能（字段中已有 //），追加的内容会落进进化形态，这种情况应改用完全替换。" : "忽略模板卡原有技能，整体写入这六个字段。"}>
        <Space.Compact>
          <Button type={skillSource === "append" ? "primary" : "default"} onClick={() => switchSkillSource("append")}>追加到模板技能</Button>
          <Button type={skillSource === "change" ? "primary" : "default"} onClick={() => switchSkillSource("change")}>完全替换模板技能</Button>
        </Space.Compact>
      </Field>
      {skillSource === "append" && <CheckboxField label="前置逗号（追加到模板已有技能）" value={groups.leadingComma} disabled={!groups.normal.length && !groups.evolved.length} onChange={(item) => setGroups({ ...groups, leadingComma: item })} />}
      <SkillGroupPanel label="普通形态" color="blue" description="进化前生效，写在 // 之前" empty="暂无普通形态技能。点击“新增技能”创建第一组并行技能字段。" rows={groups.normal} onChange={(rows) => setGroups({ ...groups, normal: rows })} />
      {groups.hasEvolution || groups.evolved.length
        ? <SkillGroupPanel label="进化形态" color="volcano" description="进化后生效，写在 // 之后" empty="进化形态已启用但没有技能条目；保存后会写出空的 // 右半段。" rows={groups.evolved} onChange={(rows) => setGroups({ ...groups, evolved: rows })} extra={<Button size="small" type="text" danger onClick={() => setGroups({ ...groups, evolved: [], hasEvolution: false })}>移除进化形态</Button>} />
        : <Button className="skill-add-evolution" type="dashed" icon={<PlusOutlined />} disabled={!groups.normal.length} onClick={() => setGroups({ ...groups, hasEvolution: true, evolved: [emptySkillRow()] })}>添加进化形态技能（写入 //）</Button>}
    </Section>
    <StringMapEditor label="其他字符串追加" field="stringAppendFields" value={Object.fromEntries(Object.entries(value.stringAppendFields).filter(([key]) => !isSkillField(key)))} onChange={(item) => set("stringAppendFields", { ...onlySkillFields(value.stringAppendFields), ...item })} valueMultiline />
    <Section title="字符串数组属性" description="stringArrayFields" collapsible defaultOpen={false}><div className="stack">{Object.entries(value.stringArrayFields).map(([key, items], index, entries) => <div className="map-row" key={`${index}-${key}`}><input list="suggestions-string-array" value={key} onChange={(event) => set("stringArrayFields", Object.fromEntries(entries.map(([oldKey, oldValue], itemIndex) => itemIndex === index ? [event.target.value, oldValue] : [oldKey, oldValue])))} /><textarea rows={2} value={items.join("\n")} onChange={(event) => set("stringArrayFields", { ...value.stringArrayFields, [key]: event.target.value.split(/\r?\n/) })} /><button type="button" className="danger" onClick={() => set("stringArrayFields", Object.fromEntries(entries.filter((_, itemIndex) => itemIndex !== index)))}>删除</button></div>)}</div><datalist id="suggestions-string-array">{cardParameterFields.stringArray.map((item) => <option key={item} value={item} />)}</datalist><button type="button" onClick={() => set("stringArrayFields", { ...value.stringArrayFields, [cardParameterFields.stringArray.find((item) => !(item in value.stringArrayFields)) ?? "Field"]: [] })}>新增数组</button></Section>
    <Section title="本地化文本" description="localizationFields"><div className="field-grid">{localizationKeys.map((key) => <TextField key={key} label={key} field={key} value={value.localizationFields[key] ?? ""} multiline={key !== "CardName"} wide={key !== "CardName"} onChange={(item) => set("localizationFields", { ...value.localizationFields, [key]: item })} />)}</div><StringMapEditor label="其他本地化字段" value={Object.fromEntries(Object.entries(value.localizationFields).filter(([key]) => !localizationKeys.includes(key)))} onChange={(item) => set("localizationFields", { ...Object.fromEntries(localizationKeys.filter((key) => key in value.localizationFields).map((key) => [key, value.localizationFields[key]])), ...item })} valueMultiline /></Section>
    <UnknownFieldsEditor value={value} knownKeys={known} onChange={(item) => onChange(item as CardMasterPatch)} />
  </div>;
}

export function CardMasterEditor({ value, onChange }: { value: CardMasterPatch[]; onChange: (value: CardMasterPatch[]) => void }) {
  const cards = useCardCatalog();
  return <div className="editor-content"><Section title="卡牌补丁" description={`${value.length} 项`} actions={<button type="button" onClick={() => onChange([...value, newCardPatch()])}>新增补丁</button>}><div className="stack">{value.map((patch, index) => <details className="boss-details" key={index} open={index === 0}><summary><span><strong>{patch.newCard ? `新卡 · ${cardTitle(cards.get(patch.cardId), patch.cardId)}` : `修改卡 · ${cardTitle(cards.get(patch.templateCardId), patch.templateCardId)}`}</strong><small>{patch.newCard ? `模板 ${cardTitle(cards.get(patch.templateCardId), patch.templateCardId)}` : `模板 ${patch.templateCardId}`}</small></span><span className="row-actions" onClick={(event) => event.preventDefault()}><RowActions index={index} count={value.length} onMove={(from, to) => onChange(moveItem(value, from, to))} onCopy={() => onChange([...value.slice(0, index + 1), structuredClone(patch), ...value.slice(index + 1)])} onDelete={() => onChange(value.filter((_, itemIndex) => itemIndex !== index))} /></span></summary><PatchForm value={patch} onChange={(item) => { const next = [...value]; next[index] = item; onChange(next); }} /></details>)}</div></Section></div>;
}
