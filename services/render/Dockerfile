# atlas-render (HU 3.5 y 6.8, ATLAS-ADR-0036 y ATLAS-ADR-0054). Se construye desde su carpeta:
#   docker build -t atlas-render services/render
# Go 1.26.10, la misma de go.mod. Binario estático: D2 acomoda con JavaScript sobre goja, en Go puro, sin navegador.
# Imágenes base fijadas por digest; la final no tiene shell y corre sin privilegios.
FROM golang:1.26.10-alpine@sha256:87c65a688975c0b1d4585d204874c45ca78354670e3a5f11368c5a492303d696 AS compilar
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY cmd ./cmd
COPY internal ./internal
RUN CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" -o /atlas-render ./cmd/atlas-render

FROM gcr.io/distroless/static-debian12:nonroot@sha256:afa5c872c891853ca7fcf1f12c3edb23f7eeef36189728842dd51042ff57f7ab
COPY --from=compilar /atlas-render /atlas-render
ENTRYPOINT ["/atlas-render"]
