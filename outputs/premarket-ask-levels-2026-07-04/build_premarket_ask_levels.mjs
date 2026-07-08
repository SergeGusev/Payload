import fs from "node:fs/promises";
import path from "node:path";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const outputDir = "D:/My/Business/PolyMarket/outputs/premarket-ask-levels-2026-07-04";

function parseCsv(text) {
  const rows = [];
  let row = [];
  let value = "";
  let inQuotes = false;

  for (let i = 0; i < text.length; i += 1) {
    const ch = text[i];
    const next = text[i + 1];

    if (inQuotes) {
      if (ch === '"' && next === '"') {
        value += '"';
        i += 1;
      } else if (ch === '"') {
        inQuotes = false;
      } else {
        value += ch;
      }
      continue;
    }

    if (ch === '"') {
      inQuotes = true;
    } else if (ch === ",") {
      row.push(value);
      value = "";
    } else if (ch === "\n") {
      row.push(value);
      rows.push(row);
      row = [];
      value = "";
    } else if (ch !== "\r") {
      value += ch;
    }
  }

  if (value.length > 0 || row.length > 0) {
    row.push(value);
    rows.push(row);
  }

  const [headers, ...dataRows] = rows.filter((r) => r.some((v) => v !== ""));
  return dataRows.map((r) => Object.fromEntries(headers.map((h, i) => [h, r[i] ?? ""])));
}

async function loadCsv(name) {
  return parseCsv(await fs.readFile(path.join(outputDir, name), "utf8"));
}

function maybeNumber(value) {
  if (value === null || value === undefined || value === "") return null;
  if (/^-?\d+(\.\d+)?$/.test(String(value))) return Number(value);
  return value;
}

function matrixFromRows(rows, headers) {
  const textHeaders = new Set(["due", "captured_at", "due_at", "snapshot_at_utc"]);
  return [
    headers,
    ...rows.map((row) => headers.map((header) => {
      if (textHeaders.has(header)) return row[header] ? `\u200B${row[header]}` : "";
      return maybeNumber(row[header]);
    })),
  ];
}

function colName(index) {
  let n = index + 1;
  let name = "";
  while (n > 0) {
    const rem = (n - 1) % 26;
    name = String.fromCharCode(65 + rem) + name;
    n = Math.floor((n - 1) / 26);
  }
  return name;
}

function writeTable(sheet, startCell, rows, headers, tableName) {
  const matrix = matrixFromRows(rows, headers);
  const startCol = startCell.match(/[A-Z]+/)[0];
  const startRow = Number(startCell.match(/\d+/)[0]);
  const startColIndex = startCol.split("").reduce((acc, ch) => acc * 26 + ch.charCodeAt(0) - 64, 0) - 1;
  const endCol = colName(startColIndex + headers.length - 1);
  const endRow = startRow + matrix.length - 1;
  const rangeAddress = `${startCell}:${endCol}${endRow}`;
  const range = sheet.getRange(rangeAddress);
  range.values = matrix;
  range.format.borders = { preset: "insideHorizontal", style: "thin", color: "#E5E7EB" };
  sheet.getRange(`${startCell}:${endCol}${startRow}`).format = {
    fill: "#1F4E78",
    font: { bold: true, color: "#FFFFFF" },
  };
  const table = sheet.tables.add(rangeAddress, true, tableName);
  table.style = "TableStyleMedium2";
  table.showFilterButton = true;
  return rangeAddress;
}

function formatCommon(sheet, maxCol, moneyColumns = [], numericColumns = []) {
  sheet.showGridLines = false;
  sheet.freezePanes.freezeRows(1);
  sheet.getUsedRange().format.autofitColumns();
  sheet.getUsedRange().format.autofitRows();
  for (const col of moneyColumns) {
    sheet.getRange(`${col}:${col}`).format.numberFormat = '"$"#,##0.00';
  }
  for (const col of numericColumns) {
    sheet.getRange(`${col}:${col}`).format.numberFormat = '0.00000000';
  }
  sheet.getRange(`A:${maxCol}`).format.font = { name: "Aptos", size: 10 };
}

const monitorSummary = await loadCsv("monitor_summary.csv");
const monitorTop5 = await loadCsv("monitor_top5_levels.csv");
const shadowCoverage = await loadCsv("shadow_coverage.csv");
const shadowLevels = await loadCsv("shadow_ask_levels.csv");

const workbook = Workbook.create();

const summary = workbook.worksheets.add("Summary");
summary.showGridLines = false;
summary.getRange("A1:H1").merge();
summary.getRange("A1").values = [["Premarket Ask Levels, 2026-07-04"]];
summary.getRange("A1").format = {
  fill: "#17365D",
  font: { bold: true, color: "#FFFFFF", size: 16 },
};
summary.getRange("A3:H8").values = [
  ["Scope", "Six 5-minute markets from 13:45 to 14:15 Europe/Sofia; BTC, ETH, SOL; Up and Down sides."],
  ["Direct CLOB monitor", "Stored the first 5 ask levels for all 36 books, plus total ask depth through 0.99."],
  ["Full historical ask ladder", "Not fully persisted for all 36 books. Exact missing historical levels cannot be reconstructed after the fact."],
  ["Server shadow snapshots", "Found stored ask ladders for 9 matching books; those snapshots contain 20 ask levels each."],
  ["Important", "Use the Direct Top5 sheet for all 36 books. Use Server Shadow Ask Levels only where coverage says a shadow snapshot exists."],
  ["Source", "Production PostgreSQL read-only plus local CLOB monitor JSONL; no production writes."],
];
summary.getRange("A3:A8").format = { fill: "#D9EAF7", font: { bold: true } };
summary.getRange("B3:H8").merge(true);
summary.getRange("B3:H8").format.wrapText = true;

const coverageAvailable = shadowCoverage.filter((r) => r.snapshot_at_utc).length;
const summaryRows = [
  { Metric: "Target books", Value: monitorSummary.length, Note: "6 markets x 3 assets x 2 sides" },
  { Metric: "Direct monitor detailed ask levels", Value: monitorTop5.length, Note: "First 5 asks for each target book" },
  { Metric: "Direct monitor depth totals", Value: monitorSummary.length, Note: "Total ask depth through 0.99 per target book" },
  { Metric: "Server shadow books with ask details", Value: coverageAvailable, Note: "Matching stored snapshots near T-30s" },
  { Metric: "Server shadow ask level rows", Value: shadowLevels.length, Note: "20 stored asks per covered shadow book" },
];
writeTable(summary, "A11", summaryRows, ["Metric", "Value", "Note"], "SummaryMetrics");
summary.getRange("A11:C16").format.borders = { preset: "all", style: "thin", color: "#CBD5E1" };
summary.getUsedRange().format.autofitColumns();
summary.getRange("B3:H8").format.columnWidth = 90;

const directSummary = workbook.worksheets.add("Direct Monitor Summary");
writeTable(
  directSummary,
  "A1",
  monitorSummary,
  [
    "seq", "market", "asset", "side", "due", "captured_at", "best_bid", "best_ask",
    "asks_0_99_levels", "depth_to_99_usd", "depth_to_99_shares",
    "five_by_6_usd", "five_by_6_worst_ask", "five_by_6_vwap", "five_by_6_levels_used",
  ],
  "DirectMonitorSummary",
);
formatCommon(directSummary, "O", ["J", "L"], ["G", "H", "K", "M", "N"]);

const directTop5 = workbook.worksheets.add("Direct Top5 Ask Levels");
writeTable(
  directTop5,
  "A1",
  monitorTop5,
  [
    "source", "seq", "market", "asset", "side", "due", "captured_at", "level",
    "price", "size", "notional_usd", "asks_0_99_levels", "depth_to_99_usd",
  ],
  "DirectTop5AskLevels",
);
formatCommon(directTop5, "M", ["K", "M"], ["I", "J"]);

const shadowCov = workbook.worksheets.add("Server Shadow Coverage");
writeTable(
  shadowCov,
  "A1",
  shadowCoverage,
  ["seq", "market", "asset", "side", "due_at", "snapshot_at_utc", "age_seconds", "outcome", "ask_levels", "bid_levels"],
  "ServerShadowCoverage",
);
formatCommon(shadowCov, "J", [], ["G"]);

const shadow = workbook.worksheets.add("Server Shadow Ask Levels");
writeTable(
  shadow,
  "A1",
  shadowLevels,
  [
    "source", "seq", "market", "asset", "side", "due_at", "snapshot_at_utc",
    "age_seconds", "level", "price", "size", "notional_usd",
  ],
  "ServerShadowAskLevels",
);
formatCommon(shadow, "L", ["L"], ["H", "J", "K"]);

await fs.mkdir(outputDir, { recursive: true });

const summaryPreview = await workbook.render({ sheetName: "Summary", autoCrop: "all", scale: 1, format: "png" });
await fs.writeFile(path.join(outputDir, "preview-summary.png"), new Uint8Array(await summaryPreview.arrayBuffer()));
const directPreview = await workbook.render({ sheetName: "Direct Top5 Ask Levels", range: "A1:M20", scale: 1, format: "png" });
await fs.writeFile(path.join(outputDir, "preview-direct-top5.png"), new Uint8Array(await directPreview.arrayBuffer()));
const shadowPreview = await workbook.render({ sheetName: "Server Shadow Ask Levels", range: "A1:L20", scale: 1, format: "png" });
await fs.writeFile(path.join(outputDir, "preview-shadow-levels.png"), new Uint8Array(await shadowPreview.arrayBuffer()));

const inspect = await workbook.inspect({
  kind: "table",
  range: "Summary!A1:H20",
  include: "values,formulas",
  tableMaxRows: 20,
  tableMaxCols: 8,
});
console.log(inspect.ndjson);

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
  options: { useRegex: true, maxResults: 300 },
  summary: "final formula error scan",
});
console.log(errors.ndjson);

const xlsx = await SpreadsheetFile.exportXlsx(workbook);
await xlsx.save(path.join(outputDir, "premarket-ask-levels-2026-07-04.xlsx"));
