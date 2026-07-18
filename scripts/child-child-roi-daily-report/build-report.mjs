import fs from "node:fs/promises";
import path from "node:path";
import {
  FileBlob,
  SpreadsheetFile,
  Workbook,
} from "@oai/artifact-tool";

const outputRoot = path.resolve(process.argv[2] ?? process.cwd());
const mode = process.argv[3] ?? "build";
const workbookFileName =
  process.argv[4] ?? "child-child-roi-best-daily-paper-pnl.xlsx";
const dataPath = path.join(outputRoot, "report-data.json");
const reportsDirectory = path.join(outputRoot, "reports");
const qaDirectory = path.join(outputRoot, "qa");
const workbookPath = path.join(
  reportsDirectory,
  workbookFileName,
);

await fs.mkdir(reportsDirectory, { recursive: true });
await fs.mkdir(qaDirectory, { recursive: true });

if (mode === "build") {
  await buildWorkbook();
} else if (mode === "verify") {
  await verifyFinalWorkbook();
} else {
  throw new Error(`Unknown mode '${mode}'.`);
}

async function buildWorkbook() {
  const report = JSON.parse(await fs.readFile(dataPath, "utf8"));
  if (report.strategies.length !== 6) {
    throw new Error(`Expected 6 strategies, found ${report.strategies.length}.`);
  }
  if (report.dates.length === 0) {
    throw new Error("No report dates were provided.");
  }

  const orderedTotals = report.strategies.map((strategy) => strategy.totalPnlUsd);
  for (let index = 1; index < orderedTotals.length; index += 1) {
    if (!(orderedTotals[index - 1] < orderedTotals[index])) {
      throw new Error("Strategy columns are not strictly ascending by total PnL.");
    }
  }

  const workbook = Workbook.create();
  const sheet = workbook.worksheets.add("Daily PnL");
  sheet.showGridLines = false;

  const strategyStartColumn = 2;
  const strategyEndColumn = 7;
  const dailyTotalColumn = 8;
  const firstDataRow = 2;
  const lastDataRow = firstDataRow + report.dates.length - 1;
  const totalRow = lastDataRow + 1;

  const headers = [
    "Date (UTC)",
    ...report.strategies.map((strategy) => strategy.name),
    "Daily Total",
  ];
  sheet.getRange("A1:H1").values = [headers];

  const rows = report.dates.map((dateText, dateIndex) => [
    new Date(`${dateText}T00:00:00Z`),
    ...report.strategies.map(
      (strategy) => strategy.dailyPnlUsd[dateIndex],
    ),
    null,
  ]);
  sheet.getRange(`A${firstDataRow}:H${lastDataRow}`).values = rows;

  const dailyTotalFormulas = report.dates.map((_, dateIndex) => {
    const row = firstDataRow + dateIndex;
    return [`=SUM(B${row}:G${row})+0*A${row}`];
  });
  sheet.getRange(`H${firstDataRow}:H${lastDataRow}`).formulas =
    dailyTotalFormulas;

  sheet.getRange(`A${totalRow}`).values = [["Category Total"]];
  const strategyTotalFormulas = [];
  for (
    let column = strategyStartColumn;
    column <= strategyEndColumn;
    column += 1
  ) {
    const letter = columnLetter(column);
    strategyTotalFormulas.push(
      `=SUM(${letter}${firstDataRow}:${letter}${lastDataRow})`,
    );
  }
  sheet.getRange(`B${totalRow}:G${totalRow}`).formulas = [
    strategyTotalFormulas,
  ];
  sheet.getRange(`H${totalRow}`).formulas = [
    [`=SUM(H${firstDataRow}:H${lastDataRow})`],
  ];

  const usedRange = sheet.getRange(`A1:H${totalRow}`);
  usedRange.format.font = { name: "Aptos", size: 10, color: "#111827" };
  usedRange.format.fill = "#FFFFFF";
  usedRange.format.verticalAlignment = "center";

  const headerRange = sheet.getRange("A1:H1");
  headerRange.format = {
    fill: "#263238",
    font: { name: "Aptos", size: 10, bold: true, color: "#FFFFFF" },
    horizontalAlignment: "center",
    verticalAlignment: "center",
    wrapText: true,
    borders: {
      bottom: { style: "medium", color: "#455A64" },
    },
  };
  headerRange.format.rowHeightPx = 62;

  const bodyRange = sheet.getRange(`A${firstDataRow}:H${lastDataRow}`);
  bodyRange.format.borders = {
    insideHorizontal: { style: "thin", color: "#E5E7EB" },
  };
  bodyRange.format.rowHeightPx = 24;

  const dateRange = sheet.getRange(`A${firstDataRow}:A${lastDataRow}`);
  dateRange.format.fill = "#F3F4F6";
  dateRange.format.horizontalAlignment = "center";
  dateRange.format.numberFormat = "yyyy-mm-dd";
  dateRange.format.borders = {
    right: { style: "thin", color: "#CBD5E1" },
  };

  const valueRange = sheet.getRange(
    `B${firstDataRow}:H${totalRow}`,
  );
  valueRange.format.horizontalAlignment = "right";
  valueRange.format.numberFormat =
    '"$"#,##0.00;[Red]-"$"#,##0.00;"$"0.00';
  valueRange.conditionalFormats.add("cellIs", {
    operator: "lessThan",
    formula: 0,
    format: {
      fill: "#FFFFFF",
      font: { color: "#C00000" },
    },
  });

  const dailyTotalRange = sheet.getRange(`H1:H${totalRow}`);
  dailyTotalRange.format.borders = {
    left: { style: "medium", color: "#94A3B8" },
  };
  sheet.getRange(`H${firstDataRow}:H${totalRow}`).format.fill = "#F3F4F6";
  sheet.getRange(`H${firstDataRow}:H${totalRow}`).format.font = {
    bold: true,
    color: "#111827",
  };

  const totalRange = sheet.getRange(`A${totalRow}:H${totalRow}`);
  totalRange.format = {
    fill: "#DCE6EC",
    font: { name: "Aptos", size: 10, bold: true, color: "#111827" },
    verticalAlignment: "center",
    borders: {
      top: { style: "medium", color: "#607D8B" },
      bottom: { style: "double", color: "#607D8B" },
    },
  };
  totalRange.format.rowHeightPx = 28;
  sheet.getRange(`A${totalRow}`).format.horizontalAlignment = "left";
  sheet.getRange(`B${totalRow}:H${totalRow}`).format.horizontalAlignment =
    "right";
  sheet.getRange(`B${totalRow}:H${totalRow}`).format.numberFormat =
    '"$"#,##0.00;[Red]-"$"#,##0.00;"$"0.00';

  sheet.getRange("A1:A1").format.columnWidthPx = 112;
  sheet.getRange("B1:G1").format.columnWidthPx = 238;
  sheet.getRange("H1:H1").format.columnWidthPx = 118;
  sheet.freezePanes.freezeRows(1);
  sheet.freezePanes.freezeColumns(1);

  const tableInspection = await workbook.inspect({
    kind: "table",
    range: `Daily PnL!A1:H${totalRow}`,
    include: "values,formulas",
    tableMaxRows: totalRow,
    tableMaxCols: 8,
    maxChars: 12000,
  });
  const errorInspection = await workbook.inspect({
    kind: "match",
    searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
    options: { useRegex: true, maxResults: 100 },
    summary: "initial formula error scan",
  });
  await fs.writeFile(
    path.join(outputRoot, "initial-workbook-inspection.ndjson"),
    `${tableInspection.ndjson}\n${errorInspection.ndjson}\n`,
    "utf8",
  );

  const preview = await workbook.render({
    sheetName: "Daily PnL",
    range: `A1:H${totalRow}`,
    scale: 1.5,
    format: "png",
  });
  await fs.writeFile(
    path.join(qaDirectory, "initial-report.png"),
    new Uint8Array(await preview.arrayBuffer()),
  );

  const output = await SpreadsheetFile.exportXlsx(workbook);
  await output.save(workbookPath);

  await fs.writeFile(
    path.join(outputRoot, "workbook-layout.json"),
    JSON.stringify(
      {
        sheetName: "Daily PnL",
        firstDataRow,
        lastDataRow,
        totalRow,
        lastColumn: columnLetter(dailyTotalColumn),
        workbookPath,
        expectedStrategyTotals: report.strategies.map((strategy) => ({
          name: strategy.name,
          totalPnlUsd: strategy.totalPnlUsd,
        })),
        expectedGrandTotalPnlUsd: report.grandTotalPnlUsd,
      },
      null,
      2,
    ),
    "utf8",
  );

  process.stdout.write(`${workbookPath}\n`);
}

async function verifyFinalWorkbook() {
  const layout = JSON.parse(
    await fs.readFile(path.join(outputRoot, "workbook-layout.json"), "utf8"),
  );
  const workbook = await SpreadsheetFile.importXlsx(
    await FileBlob.load(workbookPath),
  );
  const sheet = workbook.worksheets.getItem(layout.sheetName);

  const tableInspection = await workbook.inspect({
    kind: "table",
    sheetId: layout.sheetName,
    range: `A1:H${layout.totalRow}`,
    include: "values,formulas",
    tableMaxRows: layout.totalRow,
    tableMaxCols: 8,
    maxChars: 12000,
  });
  const formulaInspection = await workbook.inspect({
    kind: "formula",
    sheetId: layout.sheetName,
    range: `A1:H${layout.totalRow}`,
    maxChars: 6000,
    options: { maxResults: 100 },
  });
  const errorInspection = await workbook.inspect({
    kind: "match",
    searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
    options: { useRegex: true, maxResults: 100 },
    summary: "final formula error scan",
  });
  await fs.writeFile(
    path.join(outputRoot, "final-workbook-inspection.ndjson"),
    `${tableInspection.ndjson}\n${formulaInspection.ndjson}\n${errorInspection.ndjson}\n`,
    "utf8",
  );

  const preview = await workbook.render({
    sheetName: layout.sheetName,
    range: `A1:H${layout.totalRow}`,
    scale: 1.5,
    format: "png",
  });
  await fs.writeFile(
    path.join(qaDirectory, "final-report.png"),
    new Uint8Array(await preview.arrayBuffer()),
  );

  const formulas = sheet.getRange(
    `H${layout.firstDataRow}:H${layout.lastDataRow}`,
  ).formulas;
  if (formulas.length !== layout.lastDataRow - layout.firstDataRow + 1) {
    throw new Error("Daily-total formula count changed after final import.");
  }
  process.stdout.write(`${workbookPath}\n`);
}

function columnLetter(oneBasedColumn) {
  let value = oneBasedColumn;
  let result = "";
  while (value > 0) {
    value -= 1;
    result = String.fromCharCode(65 + (value % 26)) + result;
    value = Math.floor(value / 26);
  }
  return result;
}
