import fs from "node:fs/promises";
import path from "node:path";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const outputDir = "D:/My/Business/PolyMarket/outputs/premarket-full-orderbook-2026-07-04-1530";

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

async function loadCsv(fileName) {
  return parseCsv(await fs.readFile(path.join(outputDir, fileName), "utf8"));
}

function asNumber(value) {
  if (value === null || value === undefined || value === "") return null;
  const text = String(value);
  if (/^-?\d+(\.\d+)?$/.test(text)) return Number(text);
  return text;
}

function cellValue(row, header) {
  if (["captured_at", "due_local", "market_start_utc", "market_end_utc", "due_utc", "market_start_local", "market_end_local"].includes(header)) {
    return row[header] ? `\u200B${row[header]}` : "";
  }
  if (header === "token") {
    return row[header] ? `\u200B${row[header]}` : "";
  }
  return asNumber(row[header]);
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
  const matrix = [headers, ...rows.map((row) => headers.map((h) => cellValue(row, h)))];
  const startCol = startCell.match(/[A-Z]+/)[0];
  const startRow = Number(startCell.match(/\d+/)[0]);
  const startColIndex = startCol.split("").reduce((acc, ch) => acc * 26 + ch.charCodeAt(0) - 64, 0) - 1;
  const endCol = colName(startColIndex + headers.length - 1);
  const endRow = startRow + matrix.length - 1;
  const address = `${startCell}:${endCol}${endRow}`;
  const range = sheet.getRange(address);
  range.values = matrix;
  sheet.getRange(`${startCell}:${endCol}${startRow}`).format = {
    fill: "#1F4E78",
    font: { bold: true, color: "#FFFFFF" },
  };
  const table = sheet.tables.add(address, true, tableName);
  table.style = "TableStyleMedium2";
  table.showFilterButton = true;
  return address;
}

function formatSheet(sheet, maxCol, moneyCols = [], numericCols = []) {
  sheet.showGridLines = false;
  sheet.freezePanes.freezeRows(1);
  sheet.getUsedRange().format.autofitColumns();
  sheet.getUsedRange().format.autofitRows();
  sheet.getRange(`A:${maxCol}`).format.font = { name: "Aptos", size: 10 };
  for (const col of moneyCols) {
    sheet.getRange(`${col}:${col}`).format.numberFormat = '"$"#,##0.0000';
  }
  for (const col of numericCols) {
    sheet.getRange(`${col}:${col}`).format.numberFormat = "0.00000000";
  }
}

const summaryRows = await loadCsv("full_orderbook_summary.csv");
const levelRows = await loadCsv("full_orderbook_levels.csv");
const scheduleRows = await loadCsv("capture_schedule.csv");
const askRows = levelRows.filter((r) => r.book_side === "Ask");
const bidRows = levelRows.filter((r) => r.book_side === "Bid");
const errorRows = summaryRows.filter((r) => r.error && r.error.trim() !== "");

const workbook = Workbook.create();

const summary = workbook.worksheets.add("Summary");
summary.showGridLines = false;
summary.getRange("A1:H1").merge();
summary.getRange("A1").values = [["Full Premarket Order Book Capture"]];
summary.getRange("A1").format = {
  fill: "#17365D",
  font: { bold: true, color: "#FFFFFF", size: 16 },
};
summary.getRange("A3:H8").values = [
  ["Scope", "Six 5-minute Premarket windows, BTC/ETH/SOL, Up and Down outcomes, captured at T-30s."],
  ["Captured markets", "15:30-16:00 Europe/Sofia on 2026-07-04."],
  ["Data source", "Public Polymarket Gamma metadata for token ids, public CLOB /book for full books."],
  ["Book depth", "All ask and bid levels returned by CLOB were saved. No top-5 truncation in this run."],
  ["Safety", "Read-only collection only. No production database writes, service changes, Live changes, or orders."],
  ["Files", "Workbook plus CSV and JSONL raw captures are in the same output directory."],
];
summary.getRange("A3:A8").format = { fill: "#D9EAF7", font: { bold: true } };
summary.getRange("B3:H8").merge(true);
summary.getRange("B3:H8").format.wrapText = true;

const metricRows = [
  { Metric: "Captured books", Value: summaryRows.length, Note: "Expected 36 = 6 markets x 3 assets x 2 outcomes" },
  { Metric: "All level rows", Value: levelRows.length, Note: "Ask + Bid rows" },
  { Metric: "Ask rows", Value: askRows.length, Note: "All ask levels returned by CLOB" },
  { Metric: "Bid rows", Value: bidRows.length, Note: "All bid levels returned by CLOB" },
  { Metric: "Capture errors", Value: errorRows.length, Note: "Rows with non-empty error field" },
];
writeTable(summary, "A11", metricRows, ["Metric", "Value", "Note"], "CaptureMetrics");

const byAssetOutcome = [];
for (const asset of ["BTC", "ETH", "SOL"]) {
  for (const outcome of ["Up", "Down"]) {
    const rows = summaryRows.filter((r) => r.asset === asset && r.outcome === outcome);
    const askLevels = rows.reduce((acc, r) => acc + Number(r.ask_levels || 0), 0);
    const bidLevels = rows.reduce((acc, r) => acc + Number(r.bid_levels || 0), 0);
    const minBestAsk = Math.min(...rows.map((r) => Number(r.best_ask)).filter((v) => !Number.isNaN(v)));
    const maxBestAsk = Math.max(...rows.map((r) => Number(r.best_ask)).filter((v) => !Number.isNaN(v)));
    byAssetOutcome.push({
      Asset: asset,
      Outcome: outcome,
      Books: rows.length,
      AskLevels: askLevels,
      BidLevels: bidLevels,
      MinBestAsk: minBestAsk,
      MaxBestAsk: maxBestAsk,
    });
  }
}
writeTable(summary, "E11", byAssetOutcome, ["Asset", "Outcome", "Books", "AskLevels", "BidLevels", "MinBestAsk", "MaxBestAsk"], "ByAssetOutcome");
summary.getUsedRange().format.autofitColumns();
summary.getRange("B3:H8").format.columnWidth = 100;

const summarySheet = workbook.worksheets.add("Book Summary");
writeTable(
  summarySheet,
  "A1",
  summaryRows,
  [
    "seq", "market", "asset", "outcome", "captured_at", "due_local", "slug",
    "best_ask", "best_ask_size", "best_ask_notional_usd", "ask_levels", "total_ask_size", "total_ask_notional_usd",
    "best_bid", "best_bid_size", "best_bid_notional_usd", "bid_levels", "total_bid_size", "total_bid_notional_usd", "error",
  ],
  "BookSummary",
);
formatSheet(summarySheet, "T", ["J", "M", "P", "S"], ["H", "I", "L", "N", "O", "R"]);

const allLevels = workbook.worksheets.add("All Levels");
const levelHeaders = [
  "seq", "market", "asset", "outcome", "book_side", "level", "price", "size",
  "notional_usd", "cumulative_size", "cumulative_notional_usd", "captured_at", "due_local", "slug",
];
writeTable(allLevels, "A1", levelRows, levelHeaders, "AllOrderBookLevels");
formatSheet(allLevels, "N", ["I", "K"], ["G", "H", "J"]);

const asks = workbook.worksheets.add("Ask Levels");
writeTable(asks, "A1", askRows, levelHeaders, "AskLevels");
formatSheet(asks, "N", ["I", "K"], ["G", "H", "J"]);

const bids = workbook.worksheets.add("Bid Levels");
writeTable(bids, "A1", bidRows, levelHeaders, "BidLevels");
formatSheet(bids, "N", ["I", "K"], ["G", "H", "J"]);

const schedule = workbook.worksheets.add("Schedule");
writeTable(
  schedule,
  "A1",
  scheduleRows,
  ["seq", "asset", "outcome", "slug", "market_start_local", "market_end_local", "due_local", "question", "token"],
  "CaptureSchedule",
);
formatSheet(schedule, "I", [], []);
schedule.getRange("I:I").format.columnWidth = 24;

const summaryPreview = await workbook.render({ sheetName: "Summary", autoCrop: "all", scale: 1, format: "png" });
await fs.writeFile(path.join(outputDir, "preview-summary.png"), new Uint8Array(await summaryPreview.arrayBuffer()));
const allPreview = await workbook.render({ sheetName: "All Levels", range: "A1:N25", scale: 1, format: "png" });
await fs.writeFile(path.join(outputDir, "preview-all-levels.png"), new Uint8Array(await allPreview.arrayBuffer()));
const bookPreview = await workbook.render({ sheetName: "Book Summary", range: "A1:T20", scale: 1, format: "png" });
await fs.writeFile(path.join(outputDir, "preview-book-summary.png"), new Uint8Array(await bookPreview.arrayBuffer()));

const check = await workbook.inspect({
  kind: "table",
  range: "Summary!A1:K20",
  include: "values,formulas",
  tableMaxRows: 20,
  tableMaxCols: 11,
});
console.log(check.ndjson);

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
  options: { useRegex: true, maxResults: 300 },
  summary: "final formula error scan",
});
console.log(errors.ndjson);

const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(path.join(outputDir, "premarket-full-orderbook-2026-07-04-1530.xlsx"));
