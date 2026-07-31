FROM nginx:1.29-alpine
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY public /usr/share/nginx/html
CMD ["sh", "-c", "export PORT=${PORT:-80}; envsubst '$PORT' < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf && exec nginx -g 'daemon off;'"]
