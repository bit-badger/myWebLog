FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /mwl
COPY ./MyWebLog.sln                            ./
COPY ./Directory.Build.props                   ./
COPY ./MyWebLog/MyWebLog.fsproj                ./MyWebLog/
COPY ./MyWebLog.Data/MyWebLog.Data.fsproj      ./MyWebLog.Data/
COPY ./MyWebLog.Domain/MyWebLog.Domain.fsproj  ./MyWebLog.Domain/
RUN dotnet restore

COPY . ./
WORKDIR /mwl/MyWebLog
RUN dotnet publish -f net8.0 -c Release -r linux-x64

FROM alpine AS theme
RUN apk add --no-cache zip
WORKDIR /themes
COPY ./default-theme ./default-theme/
RUN zip default-theme.zip ./default-theme/*
COPY ./admin-theme ./admin-theme/
RUN zip admin-theme.zip ./admin-theme/*

FROM  mcr.microsoft.com/dotnet/aspnet:8.0 as final
WORKDIR /app
COPY --from=build /mwl/MyWebLog/bin/Release/net8.0/linux-x64/publish/ ./
COPY --from=theme /themes/*.zip /app/
RUN mkdir data themes

EXPOSE 80
CMD [ "/app/MyWebLog" ]
