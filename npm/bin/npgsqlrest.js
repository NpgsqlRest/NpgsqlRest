#!/usr/bin/env node

const path = require("path");
const os = require("os");
const { execFileSync } = require("child_process");

const binDir = __dirname;
const ext = os.type() === "Windows_NT" ? ".exe" : "";
const binary = path.join(binDir, `npgsqlrest${ext}`);

try {
    execFileSync(binary, process.argv.slice(2), { stdio: "inherit" });
} catch (err) {
    if (err.status != null) {
        process.exit(err.status);
    }
    console.error(`Failed to run ${binary}: ${err.message}`);
    process.exit(1);
}
