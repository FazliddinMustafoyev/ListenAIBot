FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish AudioBookBot.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

# Python va edge-tts o'rnatish
RUN apt-get update && apt-get install -y python3 python3-pip libgomp1 wget \
    && pip3 install edge-tts --break-system-packages

# Piper o'rnatish
RUN wget https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_linux_x86_64.tar.gz \
    && tar -xzf piper_linux_x86_64.tar.gz \
    && cp piper/piper /usr/local/bin/piper \
    && rm -rf piper piper_linux_x86_64.tar.gz

CMD ["dotnet", "AudioBookBot.dll"]