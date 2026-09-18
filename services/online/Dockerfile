FROM golang:1.27.1-bookworm AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download && go mod verify
COPY . ./
RUN CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o /online ./cmd/online

FROM debian:bookworm-slim
RUN groupadd --gid 10001 online && useradd --uid 10001 --gid 10001 --no-create-home online
COPY --from=build /online /usr/local/bin/online
USER 10001:10001
ENV ASHENWAKE_HTTP_ADDR=0.0.0.0:8088 ASHENWAKE_CONTAINER_BIND=1
EXPOSE 8088
ENTRYPOINT ["/usr/local/bin/online"]
