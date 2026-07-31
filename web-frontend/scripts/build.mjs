import { copyFileSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const sourceDirectory = resolve(fileURLToPath(new URL("..", import.meta.url)));
const outputDirectory = resolve(sourceDirectory, "dist");
const configuredApi = (process.env.LAUNCHER_API ?? "").trim().replace(/\/+$/, "");

rmSync(outputDirectory, { recursive: true, force: true });
mkdirSync(outputDirectory, { recursive: true });

for (const fileName of ["index.html", "styles.css", "app.js"]) {
  copyFileSync(resolve(sourceDirectory, fileName), resolve(outputDirectory, fileName));
}

writeFileSync(
  resolve(outputDirectory, "runtime-config.js"),
  `window.RUNTIME_CONFIG = Object.freeze({ apiBaseUrl: ${JSON.stringify(configuredApi)} });\n`,
  "utf8"
);

console.log(`Built frontend with API: ${configuredApi || "same origin"}`);
