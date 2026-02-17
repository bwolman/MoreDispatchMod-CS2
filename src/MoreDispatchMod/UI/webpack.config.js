const path = require("path");
const MOD = require("./mod.json");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");

// Support both CSII_USERDATAPATH env var and hardcoded macOS path
const CSII_USERDATAPATH = process.env.CSII_USERDATAPATH;
const OUTPUT_DIR = CSII_USERDATAPATH
  ? `${CSII_USERDATAPATH}/Mods/${MOD.id}`
  : `/Volumes/Users/micro/AppData/locallow/Colossal Order/Cities Skylines II/Mods/${MOD.id}`;

module.exports = {
  mode: "production",
  stats: "normal",
  entry: {
    [MOD.id]: "./src/index.tsx",
  },
  externalsType: "window",
  externals: {
    react: "React",
    "react-dom": "ReactDOM",
    "cs2/modding": "cs2/modding",
    "cs2/api": "cs2/api",
    "cs2/bindings": "cs2/bindings",
    "cs2/l10n": "cs2/l10n",
    "cs2/ui": "cs2/ui",
    "cs2/input": "cs2/input",
    "cs2/utils": "cs2/utils",
    "cohtml/cohtml": "cohtml/cohtml",
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: "ts-loader",
        exclude: /node_modules/,
      },
      {
        test: /\.s?css$/,
        include: path.join(__dirname, "src"),
        use: [
          MiniCssExtractPlugin.loader,
          {
            loader: "css-loader",
            options: {
              url: true,
              importLoaders: 1,
              modules: {
                auto: true,
                exportLocalsConvention: "camelCase",
                localIdentName: "[local]_[hash:base64:3]",
              },
            },
          },
          "sass-loader",
        ],
      },
    ],
  },
  resolve: {
    extensions: [".tsx", ".ts", ".js"],
    modules: ["node_modules", path.join(__dirname, "src")],
    alias: {
      "mod.json": path.resolve(__dirname, "mod.json"),
    },
  },
  output: {
    path: path.resolve(OUTPUT_DIR),
    library: {
      type: "module",
    },
    publicPath: `coui://ui-mods/`,
  },
  experiments: {
    outputModule: true,
  },
  plugins: [
    new MiniCssExtractPlugin(),
  ],
};
