# Build
FROM node:24-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY tsconfig.json ./
COPY src ./src
RUN npm run build && npm prune --omit=dev

# Run
FROM node:24-slim
ENV NODE_ENV=production \
    DATA_DIR=/data
WORKDIR /app
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
COPY package.json ./
# The database and the Teams sign-in token live here: mount a volume on it.
RUN mkdir /data && chown node:node /data
VOLUME /data
USER node
CMD ["node", "dist/index.js"]
