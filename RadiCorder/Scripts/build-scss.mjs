import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const projectRoot = path.resolve(__dirname, "..");
const inputPath = path.join(projectRoot, "Styles", "scss", "main.scss");
const outputPath = path.join(projectRoot, "wwwroot", "css", "custom.css");

async function ensureOutputDirectory() {
    await mkdir(path.dirname(outputPath), { recursive: true });
}

async function compileWithSass() {
    const sass = await import("sass");
    const result = sass.compile(inputPath, {
        style: "compressed",
        loadPaths: [path.join(projectRoot, "Styles", "scss")]
    });
    return result.css;
}

async function build() {
    await ensureOutputDirectory();
    const css = await compileWithSass();

    await writeFile(outputPath, css, "utf8");
    console.log(`[build:scss] generated: ${path.relative(projectRoot, outputPath)}`);
}

build().catch((error) => {
    console.error("[build:scss] failed", error);
    process.exit(1);
});
