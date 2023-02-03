/**
 * Copyright (c) 2020 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using PetaPoco;
using System.Collections.Generic;

namespace Simulator.Database.Services
{
    public class AssetService : IAssetService
    {
        public AssetModel Get(string assetGuid)
        {
            using (var db = DatabaseManager.Open())
            {
                var sql = Sql.Builder.Where("assetGuid = @0", assetGuid);
                return db.SingleOrDefault<AssetModel>(sql);
            }
        }

        public AssetModel GetByName(string name, string type = null)
        {
            using (var db = DatabaseManager.Open())
            {
                var sql = (PetaPoco.Sql)null;
                if (type == null){
                    sql = Sql.Builder.Where("name = @0", name);
                } else {
                    sql = Sql.Builder.Where("name = @0 AND type = @1", name, type);
                }
                return db.SingleOrDefault<AssetModel>(sql);
            }
        }

        public void Add(AssetModel asset)
        {
            using (var db = DatabaseManager.Open())
            {
                db.Insert(asset);
            }
        }

        public int Update(AssetModel asset)
        {
            using (var db = DatabaseManager.Open())
            {
                return db.Update(asset);
            }
        }

        public int Delete(string assetGuid)
        {
            using (var db = DatabaseManager.Open())
            {
                var sql = Sql.Builder.Where("assetGuid = @0", assetGuid);
                int result = db.Delete<AssetModel>(sql);
                return result;
            }
        }

        public int DeleteCategory(BundleConfig.BundleTypes type)
        {
            using (var db = DatabaseManager.Open())
            {
                var sql = Sql.Builder.Where("type = @0", BundleConfig.singularOf(type));
                int result = db.Delete<AssetModel>(sql);
                return result;
            }
        }

        public IEnumerable<AssetModel> List(BundleConfig.BundleTypes type)
        {
            using (var db = DatabaseManager.Open())
            {
                var sql = Sql.Builder.Where("type = @0", BundleConfig.singularOf(type));
                return db.Query<AssetModel>(sql);
            }
        }

    }
}
