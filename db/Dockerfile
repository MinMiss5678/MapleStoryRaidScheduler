FROM migrate/migrate
# 直接把 migrations 目錄複製進 image
# 新增 migration 只需 rebuild + push，YAML 不需要改動
COPY db/migrations /migrations
ENTRYPOINT ["/migrates"]
