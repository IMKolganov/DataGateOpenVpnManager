# Define the TARGETARCH argument
ARG TARGETARCH

# Use the .NET SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Check if the argument is passed
ARG TARGETARCH
RUN if [ -z "$TARGETARCH" ]; then echo "ERROR: TARGETARCH is not set!"; exit 1; fi
RUN echo "BUILD STAGE: TARGETARCH=${TARGETARCH}"

# Set the working directory
WORKDIR /src

# Copy the project file and restore dependencies
COPY ["DataGateCertManager/DataGateCertManager.csproj", "DataGateCertManager/"]
WORKDIR /src/DataGateCertManager
RUN dotnet restore "DataGateCertManager.csproj"

# Copy the rest of the application source code
WORKDIR /src
COPY . .

# Publish the application (framework-dependent)
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN echo "Using build configuration: $BUILD_CONFIGURATION" && \
    dotnet publish "DataGateCertManager/DataGateCertManager.csproj" \
      -c $BUILD_CONFIGURATION \
      -o /app/publish

# Use the ASP.NET runtime for the final image (framework-dependent)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

# Use root initially to allow setting permissions
USER root

# Install required packages
RUN apt-get update && \
    apt-get install -y \
    iptables \
    easy-rsa \
    openvpn \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published app
COPY --from=publish /app/publish .

LABEL maintainer="Ivan Kolganov with ❤️ via Kyle Manna's template"

# Environment for easy-rsa and OpenVPN
ENV OPENVPN=/etc/openvpn
ENV EASYRSA=/usr/share/easy-rsa \
    EASYRSA_CRL_DAYS=3650 \
    EASYRSA_PKI=$OPENVPN/pki

# Copy OpenVPN hook scripts for both TCP and UDP data directories
COPY Scripts /scripts
RUN chmod +x /scripts/*.sh

# Copy entrypoint
COPY entrypoint.sh /entrypoint.sh

# 🔧 Convert CRLF to LF just in case
RUN sed -i 's/\r$//' /entrypoint.sh

RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]