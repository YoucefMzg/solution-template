FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine

ARG GitHubToken
ENV GitHubToken=${GitHubToken}
ARG GITHUB_RUN_NUMBER
ENV GITHUB_RUN_NUMBER=${GITHUB_RUN_NUMBER}

RUN apk update \
    && apk add git \
    && apk add docker-cli \
    && apk add zip \
    && apk add bash
    
ENV PATH="$PATH:/root/.dotnet/tools:/bin"

RUN echo "${GitHubToken}" | docker login ghcr.io -u ci --password-stdin

COPY . ./repo/

RUN git config --system --add safe.directory /repo

WORKDIR /repo