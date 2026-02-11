#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const os = require("os");

const binDir = path.join(__dirname, "bin");
const ext = os.type() === "Windows_NT" ? ".exe" : "";
const binaryPath = path.join(binDir, `npgsqlrest${ext}`);

if (fs.existsSync(binaryPath)) {
    fs.unlinkSync(binaryPath);
}
