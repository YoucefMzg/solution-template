FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine

RUN apk update \
    && apk add git \
    && apk add docker-cli \
    && apk add zip \
    && apk add bash
    
ENV PATH="$PATH:/root/.dotnet/tools:/bin"

COPY . ./repo/

RUN git config --system --add safe.directory /repo

WORKDIR /repo