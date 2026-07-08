import { existsSync } from "node:fs";
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { execFile as execFileCallback } from "node:child_process";
import { promisify } from "node:util";
import { Workbook, SpreadsheetFile } from "@oai/artifact-tool";

const execFile = promisify(execFileCallback);

const outputDir = "D:\\My\\Business\\PolyMarket\\outputs\\live-daily-report-2026-07-06";
const workbookPath = join(outputDir, "current-live-daily-realized-report.xlsx");
const previewPath = join(outputDir, "current-live-daily-realized-report-preview.png");

const connectionString = process.env.POLYCOPYTRADER_POSTGRES_CONNECTION;
if (!connectionString) {
  throw new Error("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

await mkdir(outputDir, { recursive: true });

const psqlPath = [
  process.env.PSQL_PATH,
  "D:\\Program Files\\PostgreSQL\\17\\bin\\psql.exe",
  "C:\\Program Files\\PostgreSQL\\17\\bin\\psql.exe",
  "psql",
].filter(Boolean).find((candidate) => candidate === "psql" || existsSync(candidate));

if (!psqlPath) {
  throw new Error("psql was not found.");
}

function parsePostgresConnection(value) {
  if (!value.includes("://")) {
    const parts = new Map();
    for (const segment of value.split(";")) {
      const index = segment.indexOf("=");
      if (index < 0) {
        continue;
      }

      parts.set(
        segment.slice(0, index).trim().toLowerCase(),
        segment.slice(index + 1).trim(),
      );
    }

    return {
      host: process.env.PCT_REPORT_DB_HOST ?? parts.get("host") ?? "localhost",
      port: process.env.PCT_REPORT_DB_PORT ?? parts.get("port") ?? "5432",
      database: process.env.PCT_REPORT_DB_NAME ?? parts.get("database") ?? parts.get("db") ?? "",
      user: process.env.PCT_REPORT_DB_USER ?? parts.get("username") ?? parts.get("user id") ?? parts.get("user") ?? "",
      password: process.env.PCT_REPORT_DB_PASSWORD ?? parts.get("password") ?? "",
    };
  }

  const url = new URL(value);
  return {
    host: process.env.PCT_REPORT_DB_HOST ?? url.hostname,
    port: process.env.PCT_REPORT_DB_PORT ?? (url.port || "5432"),
    database: process.env.PCT_REPORT_DB_NAME ?? url.pathname.replace(/^\//, ""),
    user: process.env.PCT_REPORT_DB_USER ?? decodeURIComponent(url.username),
    password: process.env.PCT_REPORT_DB_PASSWORD ?? decodeURIComponent(url.password),
  };
}

const sql = String.raw`
WITH current_live AS (
  SELECT id, name
  FROM strategies
  WHERE live_stakes = true
),
daily AS (
  SELECT
    (live.created_at_utc AT TIME ZONE 'UTC')::date AS date_utc,
    live.strategy_id,
    SUM(COALESCE(live.realized_pnl_usd, 0)) AS live_realized
  FROM live_orders live
  JOIN current_live strategy ON strategy.id = live.strategy_id
  GROUP BY 1, live.strategy_id
),
dates AS (
  SELECT DISTINCT date_utc
  FROM daily
),
grid AS (
  SELECT
    dates.date_utc,
    strategy.id,
    strategy.name,
    COALESCE(daily.live_realized, 0) AS live_realized
  FROM dates
  CROSS JOIN current_live strategy
  LEFT JOIN daily
    ON daily.date_utc = dates.date_utc
   AND daily.strategy_id = strategy.id
),
strategy_totals AS (
  SELECT
    strategy.id,
    strategy.name,
    COALESCE(SUM(daily.live_realized), 0) AS total_live_realized
  FROM current_live strategy
  LEFT JOIN daily ON daily.strategy_id = strategy.id
  GROUP BY strategy.id, strategy.name
),
date_totals AS (
  SELECT date_utc, SUM(live_realized) AS date_total
  FROM daily
  GROUP BY date_utc
)
SELECT jsonb_build_object(
  'generatedAtUtc', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
  'liveStrategyCount', (SELECT COUNT(*) FROM current_live),
  'liveOrderCount', (
    SELECT COUNT(*)
    FROM live_orders live
    JOIN current_live strategy ON strategy.id = live.strategy_id
  ),
  'firstLiveOrderDateUtc', (
    SELECT MIN((live.created_at_utc AT TIME ZONE 'UTC')::date)
    FROM live_orders live
    JOIN current_live strategy ON strategy.id = live.strategy_id
  ),
  'lastLiveOrderDateUtc', (
    SELECT MAX((live.created_at_utc AT TIME ZONE 'UTC')::date)
    FROM live_orders live
    JOIN current_live strategy ON strategy.id = live.strategy_id
  ),
  'strategies', COALESCE((
    SELECT jsonb_agg(
      jsonb_build_object(
        'id', id,
        'name', name,
        'total', total_live_realized
      )
      ORDER BY total_live_realized ASC, name ASC
    )
    FROM strategy_totals
  ), '[]'::jsonb),
  'dates', COALESCE((
    SELECT jsonb_agg(
      jsonb_build_object(
        'date', date_utc,
        'total', date_total
      )
      ORDER BY date_utc ASC
    )
    FROM date_totals
  ), '[]'::jsonb),
  'values', COALESCE((
    SELECT jsonb_agg(
      jsonb_build_object(
        'date', date_utc,
        'strategyId', id,
        'value', live_realized
      )
      ORDER BY date_utc ASC, name ASC
    )
    FROM grid
  ), '[]'::jsonb),
  'grandTotal', COALESCE((SELECT SUM(total_live_realized) FROM strategy_totals), 0)
)::text AS report_json;
`;

const connection = parsePostgresConnection(connectionString);
const { stdout } = await execFile(psqlPath, [
  "-h", connection.host,
  "-p", connection.port,
  "-U", connection.user,
  "-d", connection.database,
  "-A",
  "-t",
  "--pset", "footer=off",
  "-v", "ON_ERROR_STOP=1",
  "-c", sql,
], {
  env: {
    ...process.env,
    PGPASSWORD: connection.password,
  },
  maxBuffer: 1024 * 1024 * 16,
});

const report = JSON.parse(stdout.trim());
const strategies = report.strategies ?? [];
const dates = report.dates ?? [];
const valuesByDateAndStrategy = new Map();

for (const item of report.values ?? []) {
  valuesByDateAndStrategy.set(`${item.date}|${item.strategyId}`, Number(item.value));
}

function columnName(index) {
  let n = index + 1;
  let name = "";
  while (n > 0) {
    const remainder = (n - 1) % 26;
    name = String.fromCharCode(65 + remainder) + name;
    n = Math.floor((n - 1) / 26);
  }
  return name;
}

function money(value) {
  return Math.round(Number(value) * 100) / 100;
}

function numeric(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

const workbook = Workbook.create();
const sheet = workbook.worksheets.add("Live Daily");
sheet.showGridlines = false;

const strategyStartCol = 1;
const totalCol = strategyStartCol + strategies.length;
const lastColIndex = totalCol;
const lastCol = columnName(lastColIndex);
const headerRow = 4;
const firstDataRow = headerRow + 1;
const totalRow = firstDataRow + dates.length;
const finalRow = Math.max(totalRow, firstDataRow);

const rows = [];
rows.push([`Current Live strategies daily realised PnL, UTC dates`]);
rows.push([
  `Generated ${report.generatedAtUtc}; live strategies: ${report.liveStrategyCount}; live orders: ${report.liveOrderCount}; source: server PostgreSQL.`,
]);
rows.push([]);
rows.push(["Date UTC", ...strategies.map((strategy) => strategy.name), "Total"]);

for (const day of dates) {
  const row = [new Date(`${day.date}T00:00:00Z`)];
  for (const strategy of strategies) {
    row.push(numeric(valuesByDateAndStrategy.get(`${day.date}|${strategy.id}`) ?? 0));
  }
  const excelRow = rows.length + 1;
  const firstStrategyCell = columnName(strategyStartCol) + excelRow;
  const lastStrategyCell = columnName(totalCol - 1) + excelRow;
  row.push(strategies.length > 0 ? `=SUM(${firstStrategyCell}:${lastStrategyCell})` : 0);
  rows.push(row);
}

const totalRowValues = ["Total"];
for (let strategyIndex = 0; strategyIndex < strategies.length; strategyIndex += 1) {
  const col = columnName(strategyStartCol + strategyIndex);
  if (dates.length > 0) {
    totalRowValues.push(`=SUM(${col}${firstDataRow}:${col}${totalRow - 1})`);
  } else {
    totalRowValues.push(0);
  }
}
if (dates.length > 0) {
  const totalColName = columnName(totalCol);
  totalRowValues.push(`=SUM(${totalColName}${firstDataRow}:${totalColName}${totalRow - 1})`);
} else {
  totalRowValues.push(0);
}
rows.push(totalRowValues);

sheet.getRange(`A1:${lastCol}${finalRow}`).values = rows;

if (lastColIndex > 0) {
  sheet.getRange(`A1:${lastCol}1`).merge();
  sheet.getRange(`A2:${lastCol}2`).merge();
}

const titleRange = sheet.getRange(`A1:${lastCol}1`);
titleRange.format.font.bold = true;
titleRange.format.font.size = 15;
titleRange.format.fill.color = "#17324D";
titleRange.format.font.color = "#FFFFFF";

const metaRange = sheet.getRange(`A2:${lastCol}2`);
metaRange.format.font.color = "#4B5563";
metaRange.format.fill.color = "#EEF3F8";

const headerRange = sheet.getRange(`A${headerRow}:${lastCol}${headerRow}`);
headerRange.format.font.bold = true;
headerRange.format.fill.color = "#D7E3F0";
headerRange.format.font.color = "#111827";
headerRange.format.wrapText = true;
headerRange.format.verticalAlignment = "Bottom";

if (dates.length > 0) {
  sheet.getRange(`A${firstDataRow}:A${totalRow - 1}`).format.numberFormat = "yyyy-mm-dd";
}

const firstMoneyCol = columnName(strategyStartCol);
if (strategies.length > 0 || dates.length > 0) {
  sheet.getRange(`${firstMoneyCol}${firstDataRow}:${lastCol}${totalRow}`).format.numberFormat =
    "#,##0.00;[Red]-#,##0.00;0.00";
}

const totalRange = sheet.getRange(`A${totalRow}:${lastCol}${totalRow}`);
totalRange.format.font.bold = true;
totalRange.format.fill.color = "#E8EEF5";

sheet.getRange(`A${headerRow}:A${totalRow}`).format.font.bold = true;
sheet.getRange(`${lastCol}${headerRow}:${lastCol}${totalRow}`).format.font.bold = true;
sheet.getRange(`A1:${lastCol}${totalRow}`).format.autofitRows();
sheet.getRange(`A1:A${totalRow}`).format.columnWidth = 12;
for (let colIndex = strategyStartCol; colIndex <= totalCol; colIndex += 1) {
  const col = columnName(colIndex);
  sheet.getRange(`${col}1:${col}${totalRow}`).format.columnWidth = colIndex === totalCol ? 13 : 24;
}
sheet.freezePanes.freezeRows(headerRow);
sheet.freezePanes.freezeColumns(1);

const xlsx = await SpreadsheetFile.exportXlsx(workbook);
await xlsx.save(workbookPath);

const inspected = await workbook.inspect({
  range: `Live Daily!A${headerRow}:${lastCol}${Math.min(totalRow, firstDataRow + 10)}`,
  include: "values,formulas,numberFormats",
});

const preview = await workbook.render({
  sheetName: "Live Daily",
  autoCrop: "all",
  scale: 1,
  format: "png",
});
await writeFile(previewPath, Buffer.from(await preview.arrayBuffer()));

console.log(JSON.stringify({
  workbookPath,
  previewPath,
  strategyCount: strategies.length,
  dateCount: dates.length,
  firstDateUtc: report.firstLiveOrderDateUtc,
  lastDateUtc: report.lastLiveOrderDateUtc,
  grandTotal: money(report.grandTotal),
  inspectRows: inspected?.cells?.length ?? inspected?.values?.length ?? null,
}, null, 2));
