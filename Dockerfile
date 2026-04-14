# 1-bosqich: Build (Sizning kodingizni kompilyatsiya qilish)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish AudioBookBot.csproj -c Release -o out

# 2-bosqich: Runtime (Dasturni ishga tushirish muhiti)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Build bosqichidan olingan fayllarni nusxalash
COPY --from=build /app/out .

# Tizim yangilanishi va kerakli paketlarni o'rnatish
# Piper uchun: libgomp1, espeak-ng, wget
# Audio uchun: ffmpeg
# Edge-TTS uchun: python3, python3-pip
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    libgomp1 \
    ffmpeg \
    espeak-ng \
    wget \
    && rm -rf /var/lib/apt/lists/*

# Edge-TTS o'rnatish
RUN pip3 install edge-tts --break-system-packages

# Piper-ni o'rnatish va kutubxonalarini sozlash
RUN wget https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_linux_x86_64.tar.gz \
    && tar -xzf piper_linux_x86_64.tar.gz \
    && cp piper/piper /usr/local/bin/piper \
    && cp piper/*.so* /usr/local/lib/ \
    && ldconfig \
    && rm -rf piper piper_linux_x86_64.tar.gz

# Modellarni konteyner ichiga nusxalash
# Diqqat: Kompyuteringizda Dockerfile yonida 'models' papkasi bo'lishi shart!
RUN mkdir -p /app/models
COPY ./models /app/models/

# Botni ishga tushirish
CMD ["dotnet", "AudioBookBot.dll"]