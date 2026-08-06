import { Alert, Typography } from "antd";
import type { ValidationIssue } from "../types";

export function ValidationPanel({ issues }: { issues: ValidationIssue[] }) {
  if (!issues.length) return <Alert className="validation-ok" type="success" showIcon message="配置校验通过" />;
  const errors = issues.filter((issue) => issue.severity === "error").length;
  const warnings = issues.filter((issue) => issue.severity === "warning").length;
  return <Alert
    className="validation-panel"
    type={errors ? "error" : "warning"}
    showIcon
    message={`发现 ${errors} 个错误，${warnings} 个警告`}
    description={<ul>{issues.map((issue, index) => <li key={index} className={issue.severity}><Typography.Text code>{issue.path}</Typography.Text>{issue.message}</li>)}</ul>}
  />;
}
