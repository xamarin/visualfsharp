FROM mono:5.20

# need `make` in order to build
RUN apt-get update && apt-get install -y build-essential && rm -rf /var/lib/apt/lists/*

WORKDIR /fsharp

COPY . .

RUN make

RUN make install