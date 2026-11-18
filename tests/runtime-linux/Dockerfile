FROM ubuntu:24.04
RUN apt-get update && apt-get install -y --no-install-recommends \
    git libicu74 libssl3t64 zlib1g libx11-6 libice6 libsm6 libfontconfig1 \
    libfreetype6 libxrandr2 libxi6 libxcursor1 libxrender1 libgl1 libegl1 \
    libsecret-1-0 dbus-x11 gnome-keyring xvfb xauth fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
RUN useradd --create-home trag
COPY ["artifacts/publish/linux-x64/", "/opt/Trag With Spaces/"]
COPY tests/runtime-linux/smoke.sh /opt/smoke.sh
RUN chmod +x '/opt/Trag With Spaces/Trag' /opt/smoke.sh
USER trag
WORKDIR /home/trag
ENTRYPOINT ["dbus-run-session", "--", "xvfb-run", "-a", "sh", "/opt/smoke.sh"]
