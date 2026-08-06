import type { ReactNode } from "react";
import { ArrowDownOutlined, ArrowUpOutlined, CopyOutlined, DeleteOutlined } from "@ant-design/icons";
import { Button, Card as AntCard, Checkbox, Collapse, Form, Input, InputNumber, Select, Space, Switch, Tooltip, Typography } from "antd";

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

export function NumberField({ label, field, value, onChange, min, max, disabled }: { label: string; field?: string; value: number; onChange: (value: number) => void; min?: number; max?: number; disabled?: boolean }) {
  return <Field label={label} field={field}>
    <InputNumber className="full-number" value={Number.isFinite(value) ? value : 0} min={min} max={max} disabled={disabled} onChange={(item) => onChange(item ?? 0)} />
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
