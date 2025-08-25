#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const os = require("os");
const https = require("https");

const downloadDir = "../.bin/";
const downloadFrom = "https://github.com/NpgsqlRest/NpgsqlRest/releases/download/v2.32.0-client-v2.27.1/";

function download(url, to, done) {
    https.get(url, (response) => {
        if (response.statusCode == 200) {
            const file = fs.createWriteStream(to, { mode: 0o755 });
            response.pipe(file);
            file.on("finish", () => {
                file.close();
                console.info(`${to} ...`,);
                if (done) {
                    done();
                }
            });
        } else if (response.statusCode == 302) {
            download(response.headers.location, to);
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
var downloadFileUrl;
var downloadTo;

if (osType === "Windows_NT") {
    downloadFileUrl = `${downloadFrom}npgsqlrest-win64.exe`;
    downloadTo = `${downloadDir}npgsqlrest.exe`;
} else if (osType === "Linux") {
    downloadFileUrl = `${downloadFrom}npgsqlrest-linux64`;
    downloadTo = `${downloadDir}npgsqlrest`;
} else if (osType === "Darwin") {
    downloadFileUrl = `${downloadFrom}npgsqlrest-osx-arm64`;
    downloadTo = `${downloadDir}npgsqlrest`;
} else {
    console.error("Unsupported OS detected:", osType);
    process.exit(1);
}

if (!fs.existsSync(path.dirname(downloadTo))) {
    fs.mkdirSync(path.dirname(downloadTo), { recursive: true });
}

if (fs.existsSync(downloadTo)) {
    fs.unlinkSync(downloadTo);
}
download(downloadFileUrl, downloadTo);


downloadFileUrl = `${downloadFrom}appsettings.json`;
downloadTo = "./appsettings.json";
if (fs.existsSync(downloadFileUrl)) {
    fs.unlinkSync(downloadFileUrl, downloadTo);
}
download(downloadFileUrl, downloadTo);
