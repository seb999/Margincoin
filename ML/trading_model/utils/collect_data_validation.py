import pandas as pd

df = pd.read_csv("ML/data/training_data.csv")  # adjust path
ada = df[df.symbol=="BTCUSDC"].sort_values("timestamp")
ts = pd.to_datetime(ada.timestamp, unit='s')
interval = pd.Timedelta('1h')  # use 30m if that’s what you want
gaps = ts.diff() > interval
print(ts[gaps])        # shows gap start times
print(ts[gaps].shift(-1))  # next bar after the gap