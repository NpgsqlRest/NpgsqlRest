FROM ubuntu:25.04 AS builder
WORKDIR /app
RUN apt-get update && \
    apt-get install -y --no-install-recommends wget ca-certificates && \
    wget https://github.com/NpgsqlRest/NpgsqlRest/releases/download/v2.32.0-client-v2.27.1/npgsqlrest-linux64 -O npgsqlrest && \
    chmod +x npgsqlrest

FROM ubuntu:25.04
WORKDIR /app
COPY --from=builder /app/npgsqlrest /usr/local/bin/npgsqlrest

RUN apt-get update && \
    apt-get install -y --no-install-recommends libssl3 ca-certificates && \
    update-ca-certificates && \
    rm -rf /var/lib/apt/lists/* && \
    apt-get clean autoclean && \
    apt-get autoremove --yes && \
    rm -rf /var/lib/{apt,dpkg,cache,log}/

ENV PATH="/usr/local/bin:${PATH}"

ENTRYPOINT ["npgsqlrest"]