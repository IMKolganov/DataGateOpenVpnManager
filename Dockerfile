# syntax=docker/dockerfile:1.7

# 1) build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["DataGateCertManager/DataGateCertManager.csproj", "DataGateCertManager/"]
WORKDIR /src/DataGateCertManager
RUN dotnet restore "DataGateCertManager.csproj"
WORKDIR /src
COPY . .

# 2) publish stage (renamed)
FROM build AS app_publish
ARG BUILD_CONFIGURATION=Release
RUN echo "Using build configuration: $BUILD_CONFIGURATION" && \
    dotnet publish "DataGateCertManager/DataGateCertManager.csproj" \
      -c $BUILD_CONFIGURATION -o /app/publish

# 3) final stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
USER root
RUN apt-get update && apt-get install -y \
    curl nano iptables easy-rsa openvpn gettext-base openvpn-dco-dkms \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=app_publish /app/publish .
COPY defaults/server.conf.template /defaults/server.conf.template
COPY scripts /scripts
COPY entrypoint.sh /entrypoint.sh
RUN sed -i 's/\r$//' /entrypoint.sh && \
    find /scripts -name '*.sh' -exec sed -i 's/\r$//' {} + && \
    sed -i 's/\r$//' /defaults/server.conf.template && \
    chmod +x /entrypoint.sh && chmod +x /scripts/*.sh
ENTRYPOINT ["/entrypoint.sh"]