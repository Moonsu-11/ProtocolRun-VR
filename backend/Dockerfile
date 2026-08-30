FROM python:3.12-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt && useradd --uid 10001 --create-home appuser && mkdir /data && chown appuser /data
COPY protocolrun ./protocolrun
COPY static ./static
USER appuser
CMD ["sh", "-c", "exec uvicorn protocolrun.api:create_app --factory --host 0.0.0.0 --port ${PORT:-8080} --workers 1"]
