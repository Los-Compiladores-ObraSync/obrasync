# Etapa 1: compilar la app con Node (misma versión que la CI).
FROM node:22-alpine AS build
WORKDIR /app
# Primero solo los archivos de dependencias: Docker reutiliza esta capa si no cambiaron.
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# Etapa 2: servir el resultado con nginx, sin usuario root.
FROM nginxinc/nginx-unprivileged:stable-alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080