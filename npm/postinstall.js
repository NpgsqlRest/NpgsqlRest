#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const os = require("os");
const https = require("https");

const downloadFrom = "https://github.com/NpgsqlRest/NpgsqlRest/releases/download/v3.8.0/";

// Download binary next to this script, not to ../.bin/
const binDir = path.join(__dirname, "bin");

function download(url, to, done) {
    https.get(url, (response) => {
        if (response.statusCode == 200) {
            const file = fs.createWriteStream(to, { mode: 0o755 });
            response.pipe(file);
            file.on("finish", () => {
                file.close();
                console.info(`Downloaded ${path.basename(to)}`);
                if (done) {
                    done();
                }
            });
        } else if (response.statusCode == 302) {
            download(response.headers.location, to, done);
        } else {
            console.error("Error downloading file:", to, response.statusCode, response.statusMessage);
        }
    }).on("error", (err) => {
        fs.unlink(to, () => {
            console.error("Error downloading file:", to, err);
        });
    });
}

const osType = os.type();
const arch = os.arch();
var binaryUrl;
var binaryName;

if (osType === "Windows_NT" && arch === "x64") {
    binaryUrl = `${downloadFrom}npgsqlrest-win64.exe`;
    binaryName = "npgsqlrest.exe";
} else if (osType === "Linux" && arch === "x64") {
    binaryUrl = `${downloadFrom}npgsqlrest-linux64`;
    binaryName = "npgsqlrest";
} else if (osType === "Linux" && arch === "arm64") {
    binaryUrl = `${downloadFrom}npgsqlrest-linux-arm64`;
    binaryName = "npgsqlrest";
} else if (osType === "Darwin" && arch === "arm64") {
    binaryUrl = `${downloadFrom}npgsqlrest-osx-arm64`;
    binaryName = "npgsqlrest";
} else {
    console.error(`Unsupported platform: ${osType} ${arch}`);
    process.exit(1);
}

if (!fs.existsSync(binDir)) {
    fs.mkdirSync(binDir, { recursive: true });
}

const binaryPath = path.join(binDir, binaryName);
if (fs.existsSync(binaryPath)) {
    fs.unlinkSync(binaryPath);
}

download(binaryUrl, binaryPath);
