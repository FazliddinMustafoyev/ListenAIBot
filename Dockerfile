FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish AudioBookBot.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

# Piper o'rnatish
RUN apt-get update && apt-get install -y wget unzip libgomp1 \
    && wget https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_linux_x86_64.tar.gz \
    && tar -xzf piper_linux_x86_64.tar.gz \
    && mv piper /usr/local/bin/ \
    && rm piper_linux_x86_64.tar.gz

CMD ["dotnet", "AudioBookBot.dll"]