#!/usr/bin/env bash
# k8s/migrate.sh — 執行 golang-migrate Job，確保每次重新跑
set -euo pipefail

NAMESPACE="maple-raid"
JOB_NAME="migrate"
YAML="$(dirname "$0")/migrate-job.yaml"

echo "==> 刪除舊 Job（如存在）..."
kubectl delete job "$JOB_NAME" -n "$NAMESPACE" --ignore-not-found

echo "==> 套用 migrate-job.yaml..."
kubectl apply -f "$YAML"

echo "==> 等待 Job 完成（最多 120 秒）..."
kubectl wait --for=condition=complete job/"$JOB_NAME" -n "$NAMESPACE" --timeout=120s

echo "✅ Migration 成功"
