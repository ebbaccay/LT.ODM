const set = (columns, rows) => ({ columns, rows });
export const cbFixtures = {
  web_rd_ins_collection_builder: [set(['collectionRecid', 'result_status', 'action_status'], [[31, 'Success', 'Existing']])],
  web_rd_get_collection_builder: [set(
    ['collectionRecid', 'conceptRecid', 'conceptName', 'customer', 'season', 'targetMarket', 'targetFob', 'activeTags', 'conceptBrief', 'suggestedProducts', 'fabricDirection', 'sustainabilityNotes', 'inspirationImages'],
    [[31, 101, 'SS27 Eco Denim Capsule', 'ADIDAS', 'SS27', 'EU', 15.5, '["Sustainable","Denim"]',
      'A relaxed, low-impact denim capsule built on recycled cotton twill and laser finishing, aimed at EU lifestyle stores.', '[]',
      '["Recycled cotton twill 11oz","Hemp blend chambray"]', '["OCS certified cotton","Laser finishing saves 60% water"]', '[]']])],
  web_rd_get_collection_builder_items: [set(
    ['itemRecid', 'conceptRecid', 'submissionRecid', 'productName', 'styleCode', 'category', 'sbu', 'country', 'imageUrl', 'fobPrice', 'costBreakdown', 'gqQid', 'hasSession', 'sessionStatus'],
    [[1, 101, 2, 'Denim Trucker Jacket', 'JK-3302', 'Outerwear', 'SBU China', 'China', '', 18.9, "[{\"item\":\"Fabric\",\"amount\":9.2},{\"item\":\"Trims\",\"amount\":1.1},{\"item\":\"Other Cost\",\"amount\":0.4},{\"item\":\"Fty FOB\",\"amount\":6.2},{\"item\":\"FTY Margin\",\"amount\":2}]", '', 0, ''],
     [2, 101, 0, 'Relaxed Taper Jean', 'DNM-2201', 'Bottoms', 'Guangzhou Apex Garments', 'China', '', 16.1, "[{\"item\":\"Fabric\",\"amount\":8.1},{\"item\":\"Trims\",\"amount\":1.2},{\"item\":\"Other Cost\",\"amount\":0.4},{\"item\":\"Fty FOB\",\"amount\":5.4},{\"item\":\"FTY Margin\",\"amount\":1}]", 'Q1', 0, ''],
     [3, 101, 7, 'Organic Pocket Tee', 'TS-2205', 'Tops', 'SBU Cambodia', 'Cambodia', '', 6.4, '[]', '', 39, 'rejected']])],
  web_rd_get_collection_builder_approved_pool: [set(
    ['submissionRecid', 'productRecid', 'sourceType', 'gqQid', 'productName', 'styleCode', 'category', 'sbu', 'country', 'imageUrl', 'fobPrice', 'costBreakdown', 'bomJson', 'cmtJson', 'rateInUsd', 'submissionStatus'],
    [[9, 4, 'manual', null, 'Cargo Pant', 'CP-4101', 'Bottoms', 'SBU Vietnam', 'Vietnam', '', 16.4, '[]', null, null, 0, 'Accepted'],
     [0, 0, 'gq', 'Q7', 'Chore Jacket', 'JK-3310', 'Outerwear', 'Guangzhou Apex Garments', 'China', '', 19.6, '[]', '[]', '[]', 7.18, 'approved']])],
  web_rd_ins_collection_builder_item: [set(['rows_affected', 'result_status'], [[1, 'Success']])],
  web_rd_del_collection_builder_item: [set(['rows_affected', 'result_status'], [[1, 'Success']])],
};
